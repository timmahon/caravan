using Caravan.Builder;
using Caravan.Components.Mock;

namespace Caravan.Core.Tests;

public class StopTests : ContextTestBase
{
    [Fact]
    public async Task Stop_ends_routing_without_failing()
    {
        await StartWithRoutes(b => b.From("direct:start")
            .To("mock:before")
            .Stop()
            .To("mock:after"));
        Mock("before").ExpectedMessageCount(1);
        Mock("after").ExpectedMessageCount(0);

        var result = await Template.SendAsync("direct:start", e => e.Message.Body = "x");

        Assert.False(result.IsFailed);
        Assert.True(result.IsRouteStop);
        await MockEndpoint.AssertIsSatisfiedAsync(Context);
    }

    [Fact]
    public async Task Stop_inside_filter_skips_the_rest_of_the_route()
    {
        await StartWithRoutes(b => b.From("direct:start")
            .Filter(Expr.Body().IsEqualTo("stop"), f => f.To("mock:stopped").Stop().To("mock:never"))
            .To("mock:after"));
        Mock("stopped").ExpectedBodiesReceived("stop");
        Mock("never").ExpectedMessageCount(0);
        Mock("after").ExpectedBodiesReceived("go");

        await Template.SendBodyAsync("direct:start", "stop");
        await Template.SendBodyAsync("direct:start", "go");

        await MockEndpoint.AssertIsSatisfiedAsync(Context);
    }

    [Fact]
    public async Task Stop_in_direct_sub_route_also_stops_the_caller()
    {
        await StartWithRoutes(b =>
        {
            b.From("direct:start").To("direct:sub").To("mock:caller-after");
            b.From("direct:sub")
                .Choice(c => c.When(Expr.Body().IsEqualTo("stop"), r => r.Stop()))
                .To("mock:sub-after");
        });
        Mock("caller-after").ExpectedBodiesReceived("go");
        Mock("sub-after").ExpectedBodiesReceived("go");

        await Template.SendBodyAsync("direct:start", "stop");
        await Template.SendBodyAsync("direct:start", "go");

        await MockEndpoint.AssertIsSatisfiedAsync(Context);
    }

    [Fact]
    public async Task Request_reply_returns_body_at_the_stop()
    {
        await StartWithRoutes(b => b.From("direct:start").SetBody(_ => "stopped here").Stop().SetBody(_ => "not reached"));

        Assert.Equal("stopped here", await Template.RequestBodyAsync<string>("direct:start", "x"));
    }
}
