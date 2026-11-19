using Caravan.Builder;
using Caravan.Components.Log;
using Caravan.Spi;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Caravan.Core.Tests;

public class PropertiesComponentTests : ContextTestBase
{
    private IPropertiesComponent Properties => Context.PropertiesComponent;

    [Fact]
    public void Resolves_keys_defaults_and_text()
    {
        Properties.InitialProperties["queue"] = "orders";

        Assert.Equal("orders", Properties.ResolveProperty("queue"));
        Assert.Null(Properties.ResolveProperty("missing"));
        Assert.Equal("orders", Properties.Resolve("queue"));
        Assert.Equal("fallback", Properties.Resolve("missing:fallback"));
        Assert.Equal("seda:orders?size=5", Properties.ResolvePlaceholders("seda:{{queue}}?size={{size:5}}"));
        Assert.Equal("x-", Properties.ResolvePlaceholders("x-{{missing:}}"));
        Assert.Equal("no placeholders", Properties.ResolvePlaceholders("no placeholders"));
        Assert.Equal("{{unterminated", Properties.ResolvePlaceholders("{{unterminated"));
        var e = Assert.Throws<ArgumentException>(() => Properties.Resolve("missing"));
        Assert.Contains("Property with key [missing] not found", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Sources_are_tried_in_precedence_order()
    {
        Properties.InitialProperties["key"] = "initial";
        Assert.Equal("initial", Properties.Resolve("key"));

        Context.PropertiesSources.Add(new DictionaryPropertiesSource(new Dictionary<string, string?> { ["key"] = "first" }));
        Context.PropertiesSources.Add(new DictionaryPropertiesSource(new Dictionary<string, string?> { ["key"] = "second", ["other"] = "second" }));
        Assert.Equal("first", Properties.Resolve("key"));
        Assert.Equal("second", Properties.Resolve("other"));
        Assert.Same(Context.PropertiesSources, Properties.Sources);

        Properties.OverrideProperties["key"] = "override";
        Assert.Equal("override", Properties.Resolve("key"));
    }

    [Fact]
    public void Placeholders_nest_in_keys_values_and_defaults()
    {
        Properties.InitialProperties["env"] = "prod";
        Properties.InitialProperties["app.prod.url"] = "https://{{host}}:{{port:443}}/";
        Properties.InitialProperties["host"] = "example.com";

        Assert.Equal("https://example.com:443/", Properties.ResolvePlaceholders("{{app.{{env}}.url}}"));
        Assert.Equal("example.com", Properties.ResolvePlaceholders("{{missing:{{host}}}}"));
        Assert.Equal("https://example.com:443/", Properties.ResolveProperty("app.prod.url"));
        Assert.Equal("a example.com b prod", Properties.ResolvePlaceholders("a {{host}} b {{env}}"));
    }

    [Fact]
    public void Circular_references_are_detected()
    {
        Properties.InitialProperties["a"] = "{{b}}";
        Properties.InitialProperties["b"] = "x{{a}}";
        Properties.InitialProperties["self"] = "{{self}}";

        Assert.Contains("Circular reference", Assert.Throws<ArgumentException>(() => Properties.Resolve("a")).Message, StringComparison.Ordinal);
        Assert.Throws<ArgumentException>(() => Properties.Resolve("self"));
    }

    [Fact]
    public void Optional_placeholders_drop_uri_options()
    {
        Properties.InitialProperties["lvl"] = "Debug";

        Assert.Equal("log:a", Properties.ParseUri("log:a?level={{?missing}}"));
        Assert.Equal("log:a?showAll=true", Properties.ParseUri("log:a?level={{?missing}}&showAll=true"));
        Assert.Equal("log:a?showAll=true", Properties.ParseUri("log:a?showAll=true&level={{?missing}}"));
        Assert.Equal("log:a?level=Debug", Properties.ParseUri("log:a?level={{?lvl}}"));
        Assert.Equal("log:", Properties.ParseUri("log:{{?missing}}"));
        Assert.Equal("text: ", Properties.ResolvePlaceholders("text: {{?missing}}"));
        Assert.Equal("dflt", Properties.ResolvePlaceholders("{{?missing:dflt}}"));
        Assert.Equal(LogLevel.Information, Context.GetEndpoint<LogEndpoint>("log:opt?level={{?missing}}").Level);
    }

    [Fact]
    public void Environment_variables_are_read_with_the_env_prefix_or_by_mode()
    {
        var name = "CARAVAN_PROPS_" + Guid.NewGuid().ToString("N").ToUpperInvariant();
        var dotted = name.ToLowerInvariant().Replace('_', '.');
        Environment.SetEnvironmentVariable(name, "fromEnv");
        try
        {
            Assert.Equal("fromEnv", Properties.ResolvePlaceholders("{{env:" + name + "}}"));
            Assert.Equal("fromEnv", Properties.ResolvePlaceholders("{{env:" + dotted + "}}"));
            Assert.Equal("dflt", Properties.ResolvePlaceholders("{{env:" + name + "_NOPE:dflt}}"));
            Assert.Equal("fromEnv", Properties.ResolveProperty("env:" + name));
            Assert.Throws<ArgumentException>(() => Properties.ResolvePlaceholders("{{env:" + name + "_NOPE}}"));

            Assert.Null(Properties.ResolveProperty(dotted));
            Properties.EnvironmentVariableMode = EnvironmentVariableMode.Fallback;
            Assert.Equal("fromEnv", Properties.ResolveProperty(dotted));
            Properties.InitialProperties[dotted] = "initial";
            Assert.Equal("initial", Properties.ResolveProperty(dotted));
            Properties.EnvironmentVariableMode = EnvironmentVariableMode.Override;
            Assert.Equal("fromEnv", Properties.ResolveProperty(dotted));
            Properties.OverrideProperties[dotted] = "override";
            Assert.Equal("override", Properties.ResolveProperty(dotted));
        }
        finally
        {
            Environment.SetEnvironmentVariable(name, null);
        }
    }

    [Fact]
    public void Sys_prefix_reads_app_context_data()
    {
        AppContext.SetData("caravan.props.test", "data");

        Assert.Equal("data", Properties.ResolvePlaceholders("{{sys:caravan.props.test}}"));
        Assert.Equal("none", Properties.ResolvePlaceholders("{{sys:caravan.props.missing:none}}"));
    }

    [Fact]
    public void Configuration_keys_map_dots_to_sections()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["app:queue"] = "orders",
                ["app:retry:count"] = "3",
                ["flat.key"] = "flat",
                ["Logging:Level"] = "Debug",
            })
            .Build();
        Properties.AddConfiguration(configuration);

        Assert.Equal("orders", Properties.Resolve("app.queue"));
        Assert.Equal("orders", Properties.Resolve("app:queue:dflt").Length > 0 ? Properties.ResolveProperty("app.queue") : null);
        Assert.Equal("3", Properties.Resolve("app.retry.count"));
        Assert.Equal("flat", Properties.Resolve("flat.key"));
        Assert.Equal("debug", Properties.ResolvePlaceholders("{{logging.level}}").ToLowerInvariant());
        Assert.Null(Properties.ResolveProperty("app"));
        Assert.Equal("seda:orders", Context.ResolvePropertyPlaceholders("seda:{{app.queue}}"));
    }

    [Fact]
    public async Task Configuration_from_the_service_provider_is_a_source()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["app:name"] = "fromServices" })
            .Build();
        await using var context = new CaravanContext(new SingleServiceProvider(configuration));

        Assert.Equal("fromServices", context.PropertiesComponent.Resolve("app.name"));
    }

    [Fact]
    public async Task Placeholders_are_resolved_in_route_ids_uris_log_messages_and_simple()
    {
        Properties.InitialProperties["routeName"] = "orders";
        Properties.InitialProperties["in"] = "direct:in";
        Properties.InitialProperties["out"] = "mock:out";
        Properties.InitialProperties["prefix"] = "Order";
        Properties.InitialProperties["node"] = "myNode";
        Properties.InitialProperties["logger"] = "audit";
        Context.Registry.Bind("myStrategy", AggregationStrategies.UseLatest());
        Properties.InitialProperties["strategy"] = "myStrategy";
        await StartWithRoutes(b => b.From("{{in}}").RouteId("{{routeName}}-route")
            .SetBody(Expr.Simple("{{prefix}} ${body}")).Id("{{node}}")
            .Log(LogLevel.Information, "{{logger}}", "{{prefix}} logged")
            .Enrich("direct:enrich", aggregationStrategyRef: "{{strategy}}")
            .To("{{out}}"));
        await Context.AddRoutesAsync(RouteBuilder.Create(b => b.From("direct:enrich").SetBody(Expr.Simple("${body}!"))));
        Mock("mock:out").ExpectedBodiesReceived("Order 1!");

        await Template.SendBodyAsync("direct:in", "1");

        await Mock("mock:out").AssertIsSatisfiedAsync();
        Assert.NotNull(Context.GetRoute("orders-route"));
        Assert.Contains(Context.RouteDefinitions.Single(r => r.Id == "orders-route").Outputs, n => n.Id == "myNode");
    }

    [Fact]
    public void Missing_placeholders_in_route_ids_fail_when_routes_are_added()
    {
        Assert.Throws<ArgumentException>(() => Context.AddRoutes(RouteBuilder.Create(b => b.From("direct:a").RouteId("{{missing}}").To("mock:a"))));
    }

    private sealed class SingleServiceProvider(IConfiguration configuration) : IServiceProvider
    {
        public object? GetService(Type serviceType) => serviceType == typeof(IConfiguration) ? configuration : null;
    }
}
