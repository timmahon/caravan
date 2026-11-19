using Caravan.Builder;
using Caravan.Impl;
using Caravan.Spi;
using static Caravan.Builder.Lang;

namespace Caravan.Core.Tests;

/// <summary>Context-wide configuration: auto startup, route filters and uuid generators.</summary>
public class GlobalConfigurationTests : ContextTestBase
{
    [Fact]
    public async Task Context_auto_startup_false_starts_no_route()
    {
        Context.AutoStartup = false;
        await StartWithRoutes(b =>
        {
            b.From("direct:a").RouteId("a").To("mock:a");
            b.From("direct:b").RouteId("b").AutoStartup(false).To("mock:b");
        });

        Assert.True(Context.IsStarted);
        Assert.NotEqual(ServiceStatus.Started, Context.GetRouteStatus("a"));
        Assert.NotEqual(ServiceStatus.Started, Context.GetRouteStatus("b"));

        await Context.StartRouteAsync("a");
        await Template.SendBodyAsync("direct:a", "x");
        Assert.Equal(1, Mock("a").ReceivedCounter);

        // Routes added to a running context follow their own option.
        await Context.AddRoutesAsync(RouteBuilder.Create(b => b.From("direct:late").RouteId("late").To("mock:late")));
        Assert.Equal(ServiceStatus.Started, Context.GetRouteStatus("late"));
    }

    [Theory]
    [InlineData("foo*", null, "foo1,foo2")]
    [InlineData("foo*", "foo2", "foo1")]
    [InlineData(null, "foo*", "bar,seda")]
    [InlineData("direct:bar", null, "bar")]
    [InlineData("seda:*,bar", null, "bar,seda")]
    [InlineData("(foo|bar).*", "foo1", "foo2,bar")]
    [InlineData("*", "*", "")]
    public async Task Route_filters_select_routes_by_id_or_endpoint(string? include, string? exclude, string expected)
    {
        Context.RouteFilterIncludePattern = include;
        Context.RouteFilterExcludePattern = exclude;
        await StartWithRoutes(b =>
        {
            b.From("direct:foo1").RouteId("foo1").To("mock:result");
            b.From("direct:foo2").RouteId("foo2").To("mock:result");
            b.From("direct:bar").RouteId("bar").To("mock:result");
            b.From("seda:in").RouteId("seda").To("mock:result");
        });

        Assert.Equal(
            expected.Split(',', StringSplitOptions.RemoveEmptyEntries).Order(StringComparer.Ordinal),
            Context.RouteDefinitions.Select(r => r.Id!).Order(StringComparer.Ordinal));
        Assert.Equal(Context.RouteDefinitions.Count, Context.Routes.Count);
    }

    [Fact]
    public async Task Route_filters_apply_to_routes_from_templates()
    {
        Context.RouteFilterExcludePattern = "skip*";
        Context.AddRoutes(RouteBuilder.Create(b =>
        {
            b.RouteTemplate("t").TemplateParameter("name").From("direct:{{name}}").RouteId("{{name}}").To("mock:{{name}}");
            b.TemplatedRoute("t", r => r.Parameter("name", "keep"));
            b.TemplatedRoute("t", r => r.Parameter("name", "skip"));
        }));
        await Context.StartAsync();
        var id = await Context.AddRouteFromTemplateAsync(null, "t", new Dictionary<string, object?> { ["name"] = "skipped" });

        Assert.Equal("skipped", id);
        Assert.Equal(["keep"], Context.RouteDefinitions.Select(r => r.Id));
    }

    [Fact]
    public async Task Uuid_generator_is_used_for_exchange_message_and_simple_ids()
    {
        Context.UuidGenerator = new SimpleUuidGenerator();
        await StartWithRoutes(b => b.From("direct:start")
            .SetHeader("uuid", Simple("${uuid}"))
            .Split(Expr.Body(), s => s.To("mock:part"))
            .To("mock:result"));

        await Template.SendBodyAsync("direct:start", new[] { "a", "b" });

        var result = Mock("result").ReceivedExchanges.Single();
        var ids = new[] { result.ExchangeId, result.Message.MessageId, (string)result.Message.Headers["uuid"]! }
            .Concat(Mock("part").ReceivedExchanges.Select(e => e.ExchangeId))
            .ToList();
        Assert.All(ids, id => Assert.Matches("^[0-9]+$", id));
        Assert.Equal(ids.Count, ids.Distinct().Count());
        Assert.Throws<ArgumentNullException>(() => Context.UuidGenerator = null!);
    }

    [Fact]
    public void Built_in_uuid_generators()
    {
        Assert.IsType<DefaultUuidGenerator>(Context.UuidGenerator);
        AssertUnique(DefaultUuidGenerator.Instance, "^[0-9a-f]{32}$");
        AssertUnique(new RandomUuidGenerator(), "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$");
        AssertUnique(new ShortUuidGenerator(), "^[0-9a-f]{16}$");
        AssertUnique(new ClassicUuidGenerator(), "^ID-[A-Za-z0-9-]+-[0-9]+-0-[0-9]+$");
        var simple = new SimpleUuidGenerator();
        Assert.Equal(["1", "2", "3"], new[] { simple.GenerateUuid(), simple.GenerateUuid(), simple.GenerateUuid() });

        static void AssertUnique(IUuidGenerator generator, string pattern)
        {
            var ids = Enumerable.Range(0, 1000).AsParallel().Select(_ => generator.GenerateUuid()).ToList();
            Assert.All(ids, id => Assert.Matches(pattern, id));
            Assert.Equal(ids.Count, ids.Distinct().Count());
        }
    }
}
