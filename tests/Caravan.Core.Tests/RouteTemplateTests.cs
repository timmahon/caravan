using Caravan.Builder;
using Caravan.Components.Mock;
using Caravan.Impl;
using Caravan.Model;
using Caravan.Spi;
using Microsoft.Extensions.Configuration;
using static Caravan.Builder.Lang;

namespace Caravan.Core.Tests;

/// <summary>Route templates (mirrors Camel's RouteTemplateTest, RouteTemplateLocalBeanTest, TemplatedRouteTest).</summary>
public class RouteTemplateTests : ContextTestBase
{
    private static void GreetTemplate(RouteBuilder b) => b.RouteTemplate("greet")
        .TemplateParameter("name", description: "the endpoint name")
        .TemplateParameter("greeting", "Hello")
        .TemplateOptionalParameter("suffix")
        .From("direct:{{name}}")
        .RouteId("{{name}}-route")
        .RouteDescription("greets on {{name}}")
        .SetBody(Expr.Simple("{{greeting}} ${body}{{suffix:}}")).Id("greet-{{name}}")
        .To("mock:{{name}}");

    [Fact]
    public async Task Templates_are_stored_but_not_started_and_routes_are_created_from_them()
    {
        Context.AddRoutes(RouteBuilder.Create(GreetTemplate));
        await Context.StartAsync();

        var template = Assert.Single(Context.RouteTemplateDefinitions);
        Assert.Equal("greet", template.Id);
        Assert.Equal(["name", "greeting", "suffix"], template.Parameters.Select(p => p.Name));
        Assert.True(template.Parameters[0].Required);
        Assert.False(template.Parameters[1].Required);
        Assert.False(template.Parameters[2].Required);
        Assert.Empty(Context.RouteDefinitions);
        Assert.Empty(Context.Routes);

        var id = await Context.AddRouteFromTemplateAsync(null, "greet", new Dictionary<string, object?> { ["name"] = "a" });
        var other = await Context.AddRouteFromTemplateAsync("b-custom", "greet", new Dictionary<string, object?> { ["name"] = "b", ["greeting"] = "Hi" });

        Assert.Equal("a-route", id);
        Assert.Equal("b-custom", other);
        Assert.Equal(ServiceStatus.Started, Context.GetRouteStatus("a-route"));
        await Template.SendBodyAsync("direct:a", "x");
        await Template.SendBodyAsync("direct:b", "y");
        Assert.Equal(["Hello x"], Mock("a").ReceivedBodies);
        Assert.Equal(["Hi y"], Mock("b").ReceivedBodies);

        var route = Context.RouteDefinitions.Single(r => r.Id == "a-route");
        Assert.Equal("greet", route.TemplateId);
        Assert.Equal("a", route.TemplateParameters!["name"]);
        Assert.Equal("Hello", route.TemplateParameters["greeting"]);
        Assert.Equal("direct:a", route.FromUri);
        Assert.Equal("greets on a", route.Description);
        Assert.Equal("greet-a", route.Outputs[0].Id);
        Assert.Equal("greet-b", Context.RouteDefinitions.Single(r => r.Id == "b-custom").Outputs[0].Id);

        // The template is untouched.
        Assert.Equal("direct:{{name}}", template.Route!.FromUri);
        Assert.Equal("greet-{{name}}", template.Route.Outputs[0].Id);
    }

    [Fact]
    public async Task Routes_from_templates_added_before_start_are_created_at_start()
    {
        Context.AddRoutes(RouteBuilder.Create(GreetTemplate));
        await Context.AddRouteFromTemplateAsync("r1", "greet", new Dictionary<string, object?> { ["name"] = "a" });
        Assert.Single(Context.RouteDefinitions);
        Assert.Null(Context.GetRoute("r1"));

        await Context.StartAsync();
        await Template.SendBodyAsync("direct:a", "x");
        Assert.Equal(["Hello x"], Mock("a").ReceivedBodies);
    }

    [Fact]
    public async Task Missing_required_parameters_fail_with_their_names()
    {
        Context.AddRoutes(RouteBuilder.Create(b => b.RouteTemplate("t")
            .TemplateParameter("first")
            .TemplateParameter("second")
            .TemplateParameter("third", "3")
            .TemplateOptionalParameter("fourth")
            .From("direct:{{first}}").To("mock:{{second}}")));
        await Context.StartAsync();

        var e = await Assert.ThrowsAsync<FailedToCreateRouteFromTemplateException>(
            () => Context.AddRouteFromTemplateAsync("r", "t", new Dictionary<string, object?> { ["unknown"] = "ignored" }));

        Assert.Equal("t", e.TemplateId);
        Assert.Equal("r", e.RouteId);
        Assert.Contains("mandatory parameters must be provided: first, second", e.Message, StringComparison.Ordinal);
        Assert.Empty(Context.RouteDefinitions);

        // A null value counts as missing; unknown parameters are ignored.
        await Assert.ThrowsAsync<FailedToCreateRouteFromTemplateException>(
            () => Context.AddRouteFromTemplateAsync("r", "t", new Dictionary<string, object?> { ["first"] = "a", ["second"] = null }));
        await Context.AddRouteFromTemplateAsync("r", "t", new Dictionary<string, object?> { ["first"] = "a", ["second"] = "b", ["extra"] = 1 });
        await Template.SendBodyAsync("direct:a", "x");
        Assert.Equal(1, Mock("b").ReceivedCounter);
    }

    [Fact]
    public async Task Unknown_template_and_duplicate_route_ids_fail()
    {
        Context.AddRoutes(RouteBuilder.Create(GreetTemplate));
        await Context.StartAsync();

        var unknown = await Assert.ThrowsAsync<FailedToCreateRouteFromTemplateException>(
            () => Context.AddRouteFromTemplateAsync("r", "nope", new Dictionary<string, object?>()));
        Assert.Contains("No route template exists with id nope", unknown.Message, StringComparison.Ordinal);

        await Context.AddRouteFromTemplateAsync("r", "greet", new Dictionary<string, object?> { ["name"] = "a" });
        var duplicate = await Assert.ThrowsAsync<FailedToCreateRouteFromTemplateException>(
            () => Context.AddRouteFromTemplateAsync("r", "greet", new Dictionary<string, object?> { ["name"] = "b" }));
        Assert.IsType<FailedToCreateRouteException>(duplicate.InnerException);
    }

    [Fact]
    public async Task Template_parameters_take_precedence_over_context_properties()
    {
        Context.PropertiesComponent.InitialProperties["greeting"] = "FromContext";
        Context.PropertiesComponent.InitialProperties["suffix"] = "!";
        Context.PropertiesComponent.InitialProperties["app.a.target"] = "mock:nested-a";
        Context.AddRoutes(RouteBuilder.Create(b => b.RouteTemplate("t")
            .TemplateParameter("name")
            .TemplateParameter("greeting", "Default")
            .TemplateOptionalParameter("suffix")
            .From("direct:{{name}}")
            .SetBody(Expr.Simple("{{greeting}} ${body}{{suffix}}"))
            .To("{{app.{{name}}.target}}")));
        await Context.StartAsync();

        await Context.AddRouteFromTemplateAsync(null, "t", new Dictionary<string, object?> { ["name"] = "a", ["greeting"] = "Param" });
        await Template.SendBodyAsync("direct:a", "x");

        // greeting: the parameter wins; suffix: not given, so the context's property; the nested key resolves after substitution.
        Assert.Equal(["Param x!"], Mock("nested-a").ReceivedBodies);
    }

    [Fact]
    public async Task Parameters_are_substituted_in_simple_predicates_options_and_typed_values()
    {
        Context.AddRoutes(RouteBuilder.Create(b => b.RouteTemplate("t")
            .TemplateParameter("name")
            .TemplateParameter("skip")
            .TemplateParameter("keep")
            .TemplateParameter("loud", "false")
            .From("direct:{{name}}")
            .Filter(Pred.Simple("${body} != '{{skip}}'"), f => f
                .SetHeader("who", Simple("{{name}}"))
                .Choice(c => c
                    .When(Simple("{{loud}} == true"), r => r.Transform(e => e.Message.GetBody<string>()!.ToUpperInvariant()))
                    .Otherwise(r => r.SetHeader("quiet", Expr.Constant(true))))
                .To("mock:{{name}}?retainFirst={{keep}}"))));
        await Context.StartAsync();

        await TemplatedRouteBuilder.Builder(Context, "t").RouteId("a").Parameter("name", "a").Parameter("skip", "no").Parameter("keep", 1).AddAsync();
        await TemplatedRouteBuilder.Builder(Context, "t").RouteId("b").Parameter("name", "b").Parameter("skip", "x").Parameter("keep", 5)
            .Parameter("loud", true).AddAsync();

        foreach (var body in new[] { "x", "no", "y" })
        {
            await Template.SendBodyAsync("direct:a", body);
            await Template.SendBodyAsync("direct:b", body);
        }

        var a = Context.GetMockEndpoint("mock:a?retainFirst=1");
        Assert.Equal(2, a.ReceivedCounter);
        Assert.Equal(1, a.RetainFirst);
        Assert.Equal(["x"], a.ReceivedBodies);
        Assert.Equal("a", a.ReceivedExchanges[0].Message.Headers["who"]);
        Assert.Equal(true, a.ReceivedExchanges[0].Message.Headers["quiet"]);
        var b = Context.GetMockEndpoint("mock:b?retainFirst=5");
        Assert.Equal(5, b.RetainFirst);
        Assert.Equal(["NO", "Y"], b.ReceivedBodies);
        Assert.Equal("b", b.ReceivedExchanges[0].Message.Headers["who"]);
    }

    [Fact]
    public async Task Optional_parameters_without_value_drop_their_uri_option()
    {
        Context.AddRoutes(RouteBuilder.Create(b => b.RouteTemplate("t")
            .TemplateParameter("name")
            .TemplateOptionalParameter("keep")
            .From("direct:{{name}}")
            .To("mock:{{name}}?retainFirst={{?keep}}")));
        await Context.StartAsync();
        await Context.AddRouteFromTemplateAsync("a", "t", new Dictionary<string, object?> { ["name"] = "a" });
        await Context.AddRouteFromTemplateAsync("b", "t", new Dictionary<string, object?> { ["name"] = "b", ["keep"] = 3 });

        await Template.SendBodyAsync("direct:a", "x");
        await Template.SendBodyAsync("direct:b", "y");

        Assert.Equal(-1, Mock("a").RetainFirst);
        Assert.Equal(1, Mock("a").ReceivedCounter);
        Assert.Equal(3, Context.GetMockEndpoint("mock:b?retainFirst=3").RetainFirst);
    }

    [Fact]
    public async Task Template_beans_are_local_to_each_route()
    {
        Context.AddRoutes(RouteBuilder.Create(b => b.RouteTemplate("counting")
            .TemplateParameter("name")
            .TemplateParameter("start", "0")
            .TemplateBean("counter", rtc => new Counter(rtc.GetProperty<int>("start")))
            .From("direct:{{name}}")
            .Bean("counter", "Next")
            .SetHeader("viaUri", Simple("${body}"))
            .To("bean:{{counter}}?method=Next")
            .SetHeader("viaSimple", Simple("${bean:{{counter}}?method=Current}"))
            .To("mock:{{name}}")));
        await Context.StartAsync();

        await Context.AddRouteFromTemplateAsync("a", "counting", new Dictionary<string, object?> { ["name"] = "a" });
        await Context.AddRouteFromTemplateAsync("b", "counting", new Dictionary<string, object?> { ["name"] = "b", ["start"] = 100 });
        await Template.SendBodyAsync("direct:a", "x");
        await Template.SendBodyAsync("direct:a", "x");
        await Template.SendBodyAsync("direct:b", "x");

        Assert.Equal([2, 4], Mock("a").ReceivedBodies);
        Assert.Equal([102], Mock("b").ReceivedBodies);
        Assert.Equal(4, Mock("a").ReceivedExchanges[1].Message.Headers["viaSimple"]);
        Assert.Equal(102, Mock("b").ReceivedExchanges[0].Message.Headers["viaSimple"]);

        // The beans are not in the context's registry, but in each route's local beans (by name and by unique name).
        Assert.Null(Context.Registry.Lookup("counter"));
        var local = Context.RouteDefinitions.Single(r => r.Id == "a").LocalBeans!;
        Assert.Same(local.Lookup("counter"), local.Lookup("counter-a"));
        Assert.Equal("counter-a", Context.RouteDefinitions.Single(r => r.Id == "a").TemplateParameters!["counter"]);
        Assert.Null(LocalBeanRegistry.Current);
    }

    [Fact]
    public async Task Local_beans_shadow_context_beans_only_in_their_route()
    {
        Context.Registry.Bind("greeter", new Greeter("Global"));
        Context.AddRoutes(RouteBuilder.Create(b =>
        {
            b.RouteTemplate("t")
                .TemplateParameter("name")
                .TemplateBean("greeter", rtc => new Greeter(rtc.GetProperty<string>("name")!))
                .From("direct:{{name}}")
                .Bean("greeter", "Greet", BeanScope.Request)
                .To("direct:plain")
                .To("mock:{{name}}");
            b.From("direct:plain").Bean("greeter", "Greet", BeanScope.Prototype).To("mock:plain");
        }));
        await Context.StartAsync();
        await Context.AddRouteFromTemplateAsync("a", "t", new Dictionary<string, object?> { ["name"] = "local" });

        await Template.SendBodyAsync("direct:local", "x");

        Assert.Equal(["Global local x"], Mock("plain").ReceivedBodies);
        Assert.Equal(["Global local x"], Mock("local").ReceivedBodies);
    }

    [Fact]
    public async Task Template_beans_from_types_and_instance_beans_given_per_route()
    {
        Context.AddRoutes(RouteBuilder.Create(b => b.RouteTemplate("t")
            .TemplateParameter("name")
            .TemplateBean("counter", typeof(Counter))
            .TemplateBean("greeter", _ => new Greeter("Template"))
            .From("direct:{{name}}")
            .Bean("greeter", "Greet")
            .SetHeader("count", Simple("${bean:{{counter}}?method=Next}"))
            .To("mock:{{name}}")));
        await Context.StartAsync();

        var given = new Greeter("Given");
        await TemplatedRouteBuilder.Builder(Context, "t").RouteId("a").Parameter("name", "a").AddAsync();
        await TemplatedRouteBuilder.Builder(Context, "t").RouteId("b").Parameter("name", "b").Bean("greeter", given).AddAsync();
        await TemplatedRouteBuilder.Builder(Context, "t").RouteId("c").Parameter("name", "c")
            .Bean("greeter", rtc => new Greeter("Factory-" + rtc.RouteId)).AddAsync();
        await Template.SendBodyAsync("direct:a", "x");
        await Template.SendBodyAsync("direct:b", "x");
        await Template.SendBodyAsync("direct:c", "x");

        Assert.Equal(["Template x"], Mock("a").ReceivedBodies);
        Assert.Equal(["Given x"], Mock("b").ReceivedBodies);
        Assert.Equal(["Factory-c x"], Mock("c").ReceivedBodies);
        Assert.Equal(1, Mock("b").ReceivedExchanges[0].Message.Headers["count"]);
        Assert.Same(given, Context.RouteDefinitions.Single(r => r.Id == "b").LocalBeans!.Lookup("greeter"));
    }

    [Fact]
    public async Task Templated_route_builder_runs_configurers_and_handler()
    {
        Context.AddRoutes(RouteBuilder.Create(b => b.RouteTemplate("t")
            .TemplateParameter("name")
            .TemplateParameter("greeting")
            .Configure(rtc => rtc.SetParameter("target", "mock:" + rtc.GetProperty("name")))
            .From("direct:{{name}}")
            .SetBody(Simple("{{greeting}} ${body}"))
            .To("{{target}}")));
        await Context.StartAsync();

        var id = await TemplatedRouteBuilder.Builder(Context, "t")
            .Parameters(new Dictionary<string, object?> { ["name"] = "a" })
            .Configure(rtc => rtc.SetParameter("greeting", "Configured"))
            .Handler(route =>
            {
                route.Description = "handled";
                route.Outputs.Add(new ToDefinition("mock:handled"));
            })
            .AddAsync();
        await Template.SendBodyAsync("direct:a", "x");

        Assert.StartsWith("route", id, StringComparison.Ordinal);
        Assert.Equal(["Configured x"], Mock("a").ReceivedBodies);
        Assert.Equal(1, Mock("handled").ReceivedCounter);
        Assert.Equal("handled", Context.RouteDefinitions.Single().Description);
        Assert.Equal(2, Context.RouteTemplateDefinitions.Single().Route!.Outputs.Count);
    }

    [Fact]
    public async Task Templated_routes_declared_in_builders_including_templates_from_another_builder()
    {
        // A "library" builder holding the template, and an application builder using it.
        Context.AddRoutes(RouteBuilder.Create(GreetTemplate));
        Context.AddRoutes(RouteBuilder.Create(b =>
        {
            b.TemplatedRoute("greet", r => r.RouteId("one").Parameter("name", "one"));
            b.TemplatedRoute("greet", r => r.Parameter("name", "two").Parameter("greeting", "Hey").Bean("unused", new object()));
            b.TemplatedRoute("local", r => r.RouteId("three"));
            b.RouteTemplate("local").From("direct:three").To("mock:three");
        }));

        await Context.StartAsync();
        await Template.SendBodyAsync("direct:one", "x");
        await Template.SendBodyAsync("direct:two", "y");
        await Template.SendBodyAsync("direct:three", "z");

        Assert.Equal(["Hello x"], Mock("one").ReceivedBodies);
        Assert.Equal(["Hey y"], Mock("two").ReceivedBodies);
        Assert.Equal(1, Mock("three").ReceivedCounter);
        Assert.Equal(["one", "two-route", "three"], Context.RouteDefinitions.Select(r => r.Id));
    }

    [Fact]
    public async Task Templated_routes_added_to_a_running_context_start_at_once()
    {
        Context.AddRoutes(RouteBuilder.Create(GreetTemplate));
        await Context.StartAsync();

        await Context.AddRoutesAsync(RouteBuilder.Create(b => b.TemplatedRoute("greet", r => r.Parameter("name", "late"))));

        Assert.Equal(ServiceStatus.Started, Context.GetRouteStatus("late-route"));
        await Template.SendBodyAsync("direct:late", "x");
        Assert.Equal(1, Mock("late").ReceivedCounter);
    }

    [Fact]
    public async Task Builder_error_handling_applies_to_its_templates()
    {
        Context.AddRoutes(RouteBuilder.Create(b =>
        {
            b.OnException<InvalidOperationException>(o => o.Handled(true).To("mock:error"));
            b.RouteTemplate("t").TemplateParameter("name")
                .From("direct:{{name}}")
                .ThrowException(new InvalidOperationException("boom"));
        }));
        await Context.StartAsync();
        await Context.AddRouteFromTemplateAsync("a", "t", new Dictionary<string, object?> { ["name"] = "a" });
        await Context.AddRouteFromTemplateAsync("b", "t", new Dictionary<string, object?> { ["name"] = "b" });

        await Template.SendBodyAsync("direct:a", "x");
        await Template.SendBodyAsync("direct:b", "y");

        Assert.Equal(["x", "y"], Mock("error").ReceivedBodies);
    }

    [Fact]
    public async Task Removing_a_template_keeps_its_routes_and_templates_can_be_replaced()
    {
        Context.AddRoutes(RouteBuilder.Create(GreetTemplate));
        await Context.StartAsync();
        await Context.AddRouteFromTemplateAsync(null, "greet", new Dictionary<string, object?> { ["name"] = "a" });

        var replacement = new RouteTemplateDefinition("greet");
        replacement.Parameters.Add(new RouteTemplateParameterDefinition("name"));
        new RouteTemplateBuilder(replacement).From("direct:{{name}}").To("mock:replaced-{{name}}");
        Context.AddRouteTemplateDefinition(replacement);
        Assert.Same(replacement, Assert.Single(Context.RouteTemplateDefinitions));
        await Context.AddRouteFromTemplateAsync("b", "greet", new Dictionary<string, object?> { ["name"] = "b" });

        Assert.True(Context.RemoveRouteTemplateDefinition("greet"));
        Assert.False(Context.RemoveRouteTemplateDefinition("greet"));
        Assert.Empty(Context.RouteTemplateDefinitions);

        await Template.SendBodyAsync("direct:a", "x");
        await Template.SendBodyAsync("direct:b", "y");
        Assert.Equal(1, Mock("a").ReceivedCounter);
        Assert.Equal(1, Mock("replaced-b").ReceivedCounter);
    }

    [Fact]
    public async Task Template_without_route_and_failing_bean_fail_the_creation()
    {
        Context.AddRouteTemplateDefinition(new RouteTemplateDefinition("empty"));
        Context.AddRoutes(RouteBuilder.Create(b => b.RouteTemplate("bad")
            .TemplateBean<object>("boom", _ => throw new InvalidOperationException("no bean"))
            .From("direct:bad").To("mock:bad")));
        await Context.StartAsync();

        var empty = await Assert.ThrowsAsync<FailedToCreateRouteFromTemplateException>(
            () => Context.AddRouteFromTemplateAsync("r", "empty", new Dictionary<string, object?>()));
        Assert.Contains("no route", empty.Message, StringComparison.Ordinal);
        var bad = await Assert.ThrowsAsync<FailedToCreateRouteFromTemplateException>(
            () => Context.AddRouteFromTemplateAsync("r", "bad", new Dictionary<string, object?>()));
        Assert.IsType<InvalidOperationException>(bad.InnerException);
        Assert.Empty(Context.RouteDefinitions);
    }

    [Fact]
    public async Task Parameter_sources_from_configuration_create_routes_at_start()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["caravan:templatedRoutes:fast:templateId"] = "greet",
            ["caravan:templatedRoutes:fast:parameters:name"] = "fast",
            ["caravan:templatedRoutes:fast:parameters:greeting"] = "Quick",
            ["caravan:templatedRoutes:slow:templateId"] = "greet",
            ["caravan:templatedRoutes:slow:name"] = "slow",
            ["caravan:templatedRoutes:0:routeId"] = "indexed",
            ["caravan:templatedRoutes:0:templateId"] = "greet",
            ["caravan:templatedRoutes:0:name"] = "indexed",
        }).Build();
        await using var context = new CaravanContext(new ConfigurationServices(configuration));
        context.AddRoutes(RouteBuilder.Create(GreetTemplate));
        var source = Assert.Single(context.RouteTemplateParameterSources);
        Assert.Equal(["fast", "indexed", "slow"], source.RouteIds().Order(StringComparer.Ordinal));

        await context.StartAsync();
        await using var template = context.CreateProducerTemplate();
        await template.SendBodyAsync("direct:fast", "x");
        await template.SendBodyAsync("direct:slow", "y");
        await template.SendBodyAsync("direct:indexed", "z");

        Assert.Equal(["Quick x"], context.GetMockEndpoint("mock:fast").ReceivedBodies);
        Assert.Equal(["Hello y"], context.GetMockEndpoint("mock:slow").ReceivedBodies);
        Assert.Equal(["Hello z"], context.GetMockEndpoint("mock:indexed").ReceivedBodies);
        Assert.Equal(["fast", "indexed", "slow"], context.RouteDefinitions.Select(r => r.Id).Order(StringComparer.Ordinal));

        // A restart does not create them again.
        await context.StopAsync();
        await context.StartAsync();
        Assert.Equal(3, context.RouteDefinitions.Count);
    }

    [Fact]
    public async Task Parameter_sources_from_the_registry_and_the_context()
    {
        Context.Registry.Bind("source", new FixedSource("fromRegistry", new() { ["templateId"] = "greet", ["name"] = "reg" }));
        Context.RouteTemplateParameterSources.Add(new FixedSource("fromList", new() { ["templateId"] = "greet", ["name"] = "list" }));
        Context.AddRoutes(RouteBuilder.Create(GreetTemplate));
        await Context.StartAsync();

        await Template.SendBodyAsync("direct:reg", "x");
        await Template.SendBodyAsync("direct:list", "y");
        Assert.Equal(1, Mock("reg").ReceivedCounter);
        Assert.Equal(1, Mock("list").ReceivedCounter);
    }

    [Fact]
    public async Task Parameter_source_without_template_id_fails_the_start()
    {
        Context.RouteTemplateParameterSources.Add(new FixedSource("r", new() { ["name"] = "x" }));
        await Assert.ThrowsAsync<FailedToCreateRouteFromTemplateException>(() => Context.StartAsync());
    }

    [Fact]
    public void Template_context_converts_parameters()
    {
        Context.PropertiesComponent.InitialProperties["base"] = "10";
        var templateContext = new RouteTemplateContext(Context)
            .SetParameter("count", "{{base}}")
            .SetParameter("flag", true)
            .SetParameter("none", null);

        Assert.Equal(10, templateContext.GetProperty<int>("count"));
        Assert.True(templateContext.GetProperty<bool>("flag"));
        Assert.Null(templateContext.GetProperty("none"));
        Assert.Null(templateContext.GetProperty("missing"));
        Assert.Equal(0, templateContext.GetProperty<int>("missing"));
        Assert.False(templateContext.HasParameter("none"));
        Assert.True(templateContext.HasParameter("COUNT"));
    }

    public sealed class Counter(int start = 0)
    {
        private int _count = start;

        public int Next(string body) => Interlocked.Increment(ref _count);

        public int Current() => Volatile.Read(ref _count);
    }

    public sealed class Greeter(string greeting)
    {
        public string Greet(string body) => $"{greeting} {body}";
    }

    private sealed class FixedSource(string routeId, Dictionary<string, object?> parameters) : IRouteTemplateParameterSource
    {
        public IReadOnlyCollection<string> RouteIds() => [routeId];

        public IReadOnlyDictionary<string, object?> Parameters(string id) => parameters;
    }

    private sealed class ConfigurationServices(IConfiguration configuration) : IServiceProvider
    {
        public object? GetService(Type serviceType) => serviceType == typeof(IConfiguration) ? configuration : null;
    }
}
