using Caravan.Builder;
using Caravan.Spi;

namespace Caravan.Core.Tests;

public class ControlBusTests : ContextTestBase
{
    [Fact]
    public async Task Route_actions_stop_start_suspend_and_resume_routes()
    {
        await StartWithRoutes(b => b.From("seda:work").RouteId("work").To("mock:work"));

        await Template.SendBodyAsync("controlbus:route?routeId=work&action=stop", null);
        Assert.Equal(ServiceStatus.Stopped, Context.GetRouteStatus("work"));
        Assert.Equal("Stopped", await Template.RequestBodyAsync("controlbus:route?routeId=work&action=status", null));

        await Template.SendBodyAsync("controlbus:route?routeId=work&action=start", null);
        Assert.Equal(ServiceStatus.Started, Context.GetRouteStatus("work"));

        await Template.SendBodyAsync("controlbus:route?routeId=work&action=suspend", null);
        Assert.Equal("Suspended", await Template.RequestBodyAsync("controlbus:route?routeId=work&action=status", null));

        await Template.SendBodyAsync("controlbus:route?routeId=work&action=resume", null);
        Assert.Equal("Started", await Template.RequestBodyAsync("controlbus:route?routeId=work&action=status", null));
    }

    [Fact]
    public async Task Restart_stops_and_starts_the_route_after_the_delay()
    {
        await StartWithRoutes(b => b.From("seda:work").RouteId("work").To("mock:work"));
        var mock = Mock("mock:work").ExpectedBodiesReceived("after restart");

        await Template.SendBodyAsync("controlbus:route?routeId=work&action=restart&restartDelay=0", "body kept");
        await Template.SendBodyAsync("seda:work", "after restart");

        Assert.Equal(ServiceStatus.Started, Context.GetRouteStatus("work"));
        await mock.AssertIsSatisfiedAsync();
    }

    [Fact]
    public async Task Stats_report_route_and_context_counters()
    {
        Context.Name = "ctx";
        await StartWithRoutes(b =>
        {
            b.From("direct:ok").RouteId("ok").Log("fine");
            b.From("direct:bad").RouteId("bad").Process(_ => throw new InvalidOperationException());
        });
        await Template.SendBodyAsync("direct:ok", 1);
        await Template.SendBodyAsync("direct:ok", 2);
        await Assert.ThrowsAsync<CaravanExecutionException>(() => Template.SendBodyAsync("direct:bad", 3));

        var route = (string)(await Template.RequestBodyAsync("controlbus:route?routeId=ok&action=stats", null))!;
        var context = (string)(await Template.RequestBodyAsync("controlbus:route?action=stats", null))!;

        Assert.Equal("<routeStat id=\"ok\" state=\"Started\" exchangesInflight=\"0\" exchangesTotal=\"2\" exchangesCompleted=\"2\" exchangesFailed=\"0\"/>", route);
        Assert.StartsWith("<camelContextStat id=\"ctx\" state=\"Started\" exchangesInflight=\"0\" exchangesTotal=\"3\" exchangesCompleted=\"2\" exchangesFailed=\"1\">", context, StringComparison.Ordinal);
        Assert.Contains("<routeStat id=\"bad\" state=\"Started\" exchangesInflight=\"0\" exchangesTotal=\"1\" exchangesCompleted=\"0\" exchangesFailed=\"1\"/>", context, StringComparison.Ordinal);
        Assert.Equal("Started", await Template.RequestBodyAsync("controlbus:route?action=status", null));
    }

    [Fact]
    public async Task A_route_can_stop_itself_asynchronously()
    {
        await StartWithRoutes(b => b.From("seda:self").RouteId("self").To("controlbus:route?routeId=current&action=stop&async=true").To("mock:done"));
        var mock = Mock("mock:done").ExpectedMessageCount(1);

        await Template.SendBodyAsync("seda:self", "stop me");

        await mock.AssertIsSatisfiedAsync();
        await TestHelpers.WaitUntilAsync(() => Context.GetRouteStatus("self") == ServiceStatus.Stopped);
    }

    [Fact]
    public async Task Language_command_evaluates_the_body()
    {
        Context.Name = "bus";
        await Context.StartAsync();

        Assert.Equal("bus says hi", await Template.RequestBodyAsync("controlbus:language:simple", "${camelId} says hi"));
        Assert.Equal("hdr", await Template.RequestBodyAndHeaderAsync("controlbus:language:header", "h", "h", "hdr"));
    }

    [Fact]
    public async Task Invalid_uses_fail()
    {
        await StartWithRoutes(b => b.From("direct:x").RouteId("x").Log("x"));

        Assert.Throws<ResolveEndpointFailedException>(() => Context.GetEndpoint("controlbus:nope"));
        Assert.Throws<ArgumentException>(() => Context.GetEndpoint("controlbus:route?routeId=x&action=explode"));
        var missing = await Assert.ThrowsAsync<CaravanExecutionException>(() => Template.RequestBodyAsync("controlbus:route?routeId=none&action=status", null));
        Assert.IsType<ArgumentException>(missing.InnerException);
        await Assert.ThrowsAsync<CaravanExecutionException>(() => Template.RequestBodyAsync("controlbus:route?action=stop", null));
    }
}
