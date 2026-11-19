using Caravan.Builder;
using Caravan.Components.Kamelet;
using static Caravan.Builder.Lang;

namespace Caravan.Core.Tests;

/// <summary>The <c>kamelet</c> component (mirrors Camel's KameletTest, producer side).</summary>
public class KameletTests : ContextTestBase
{
    private static void Kamelets(RouteBuilder b)
    {
        b.RouteTemplate("prefix")
            .TemplateParameter("prefix")
            .TemplateParameter("separator", "-")
            .TemplateBean("calls", _ => new Calls())
            .From("kamelet:source")
            .SetBody(Simple("{{prefix}}{{separator}}${body}"))
            .SetHeader("calls", Simple("${bean:{{calls}}?method=Next}"))
            .To("kamelet:sink");
    }

    [Fact]
    public async Task Kamelet_calls_a_route_created_from_the_template_and_returns_to_the_caller()
    {
        await StartWithRoutes(b =>
        {
            Kamelets(b);
            b.From("direct:start").To("kamelet:prefix?prefix=A").To("mock:result");
        });

        await Template.SendBodyAsync("direct:start", "x");
        await Template.SendBodyAsync("direct:start", "y");

        Assert.Equal(["A-x", "A-y"], Mock("result").ReceivedBodies);
        Assert.Equal(2, Mock("result").ReceivedExchanges[1].Message.Headers["calls"]);
        var route = Assert.Single(Context.RouteDefinitions, r => r.TemplateId == "prefix");
        Assert.Equal("prefix-1", route.Id);
        Assert.Equal("kamelet:source?routeId=prefix-1", route.FromUri);
    }

    [Fact]
    public async Task One_route_per_distinct_parameter_set()
    {
        await StartWithRoutes(b =>
        {
            Kamelets(b);
            b.From("direct:a").To("kamelet:prefix?prefix=A").To("mock:a");
            b.From("direct:a2").To("kamelet:prefix?prefix=A").To("mock:a");
            b.From("direct:b").To("kamelet:prefix?prefix=B&separator=:").To("mock:b");
        });

        await Template.SendBodyAsync("direct:a", "1");
        await Template.SendBodyAsync("direct:a2", "2");
        await Template.SendBodyAsync("direct:b", "3");

        Assert.Equal(["A-1", "A-2"], Mock("a").ReceivedBodies);
        Assert.Equal(["B:3"], Mock("b").ReceivedBodies);
        Assert.Equal(2, Context.RouteDefinitions.Count(r => r.TemplateId == "prefix"));

        // Each route has its own local beans.
        Assert.Equal(2, Mock("a").ReceivedExchanges[1].Message.Headers["calls"]);
        Assert.Equal(1, Mock("b").ReceivedExchanges[0].Message.Headers["calls"]);
    }

    [Fact]
    public async Task Route_id_in_the_path_or_option_and_parameters_from_properties()
    {
        Context.PropertiesComponent.InitialProperties["caravan.kamelet.prefix.prefix"] = "P";
        Context.PropertiesComponent.InitialProperties["caravan.kamelet.prefix.separator"] = "+";
        await StartWithRoutes(b =>
        {
            Kamelets(b);
            b.From("direct:path").To("kamelet:prefix/fromPath").To("mock:path");
            b.From("direct:option").To("kamelet:prefix?routeId=fromOption&separator=*").To("mock:option");
        });

        await Template.SendBodyAsync("direct:path", "x");
        await Template.SendBodyAsync("direct:option", "y");

        Assert.Equal(["P+x"], Mock("path").ReceivedBodies);
        Assert.Equal(["P*y"], Mock("option").ReceivedBodies);
        Assert.NotNull(Context.GetRoute("fromPath"));
        Assert.NotNull(Context.GetRoute("fromOption"));
    }

    [Fact]
    public async Task Missing_parameters_and_unknown_templates_fail_the_exchange()
    {
        await StartWithRoutes(b =>
        {
            Kamelets(b);
            b.From("direct:missing").To("kamelet:prefix").To("mock:never");
            b.From("direct:unknown").To("kamelet:nope?x=1").To("mock:never");
        });

        var missing = await Assert.ThrowsAsync<CaravanExecutionException>(() => Template.SendBodyAsync("direct:missing", "x"));
        var unknown = await Assert.ThrowsAsync<CaravanExecutionException>(() => Template.SendBodyAsync("direct:unknown", "x"));

        Assert.Contains("mandatory parameters must be provided: prefix", Assert.IsType<FailedToCreateRouteFromTemplateException>(missing.InnerException).Message, StringComparison.Ordinal);
        Assert.IsType<FailedToCreateRouteFromTemplateException>(unknown.InnerException);
        Assert.Equal(0, Mock("never").ReceivedCounter);
    }

    [Fact]
    public async Task Concurrent_first_calls_create_the_route_once()
    {
        await StartWithRoutes(b =>
        {
            Kamelets(b);
            b.From("direct:start").To("kamelet:prefix?prefix=C").To("mock:result");
        });

        await Task.WhenAll(Enumerable.Range(0, 20).Select(i => Task.Run(() => Template.SendBodyAsync("direct:start", i))));

        Assert.Equal(20, Mock("result").ReceivedCounter);
        Assert.Single(Context.RouteDefinitions, r => r.TemplateId == "prefix");
    }

    [Fact]
    public async Task Kamelet_routes_survive_route_filters_and_restarts()
    {
        Context.RouteFilterIncludePattern = "caller";
        await StartWithRoutes(b =>
        {
            Kamelets(b);
            b.From("direct:start").RouteId("caller").To("kamelet:prefix?prefix=F").To("mock:result");
            b.From("direct:other").RouteId("other").To("mock:other");
        });

        await Template.SendBodyAsync("direct:start", "x");
        await Context.StopAsync();
        await Context.StartAsync();
        await Template.SendBodyAsync("direct:start", "y");

        Assert.Equal(["F-x", "F-y"], Mock("result").ReceivedBodies);
        Assert.Null(Context.GetRoute("other"));
        Assert.Equal(2, Context.RouteDefinitions.Count);
    }

    [Fact]
    public void Source_and_sink_endpoints_are_not_general_purpose()
    {
        Assert.Throws<ResolveEndpointFailedException>(() => Context.GetEndpoint("kamelet:source"));
        Assert.Throws<NotSupportedException>(() => Context.GetEndpoint("kamelet:source?routeId=r").CreateProducer());
        Assert.Throws<NotSupportedException>(() => Context.GetEndpoint("kamelet:sink").CreateConsumer(new Spi.DelegateProcessor(_ => ValueTask.CompletedTask)));
        Assert.Throws<NotSupportedException>(() => Context.GetEndpoint("kamelet:t?x=1").CreateConsumer(new Spi.DelegateProcessor(_ => ValueTask.CompletedTask)));
        var endpoint = Context.GetEndpoint<KameletEndpoint>("kamelet:t/r?x=1&y=2");
        Assert.Equal("t", endpoint.TemplateId);
        Assert.Equal("r", endpoint.RouteId);
        Assert.Equal("1", endpoint.Parameters["x"]);
    }

    public sealed class Calls
    {
        private int _count;

        public int Next() => Interlocked.Increment(ref _count);
    }
}
