using Caravan.Builder;
using Caravan.Components.Seda;
using Caravan.Impl;
using Caravan.Spi;
using static Caravan.Core.Tests.TestHelpers;

namespace Caravan.Core.Tests;

public class ShutdownTests : ContextTestBase
{
    [Fact]
    public async Task Context_stop_waits_for_inflight_exchanges()
    {
        var entered = NewSignal();
        var gate = NewSignal();
        await StartWithRoutes(b => b.From("direct:a").Process(async _ =>
        {
            entered.SetResult();
            await gate.Task;
        }).To("mock:result"));
        var send = Template.SendBodyAsync("direct:a", "x");
        await entered.Task.Bounded();

        var stop = Context.StopAsync();
        Assert.False(stop.IsCompleted);
        Assert.False(Context.ShutdownToken.IsCancellationRequested);
        gate.SetResult();
        await stop.Bounded();
        await send.Bounded();

        Assert.Equal(1, Mock("result").ReceivedCounter);
        Assert.False(Context.ShutdownStrategy.HasTimeoutOccurred);
    }

    [Fact]
    public async Task Consumers_are_stopped_before_waiting_for_inflight_exchanges()
    {
        var entered = NewSignal();
        var gate = NewSignal();
        await StartWithRoutes(b => b.From("direct:a").Process(async _ =>
        {
            entered.SetResult();
            await gate.Task;
        }));
        var send = Template.SendBodyAsync("direct:a", "x");
        await entered.Task.Bounded();

        var stop = Context.StopAsync();
        var e = await Assert.ThrowsAsync<CaravanExecutionException>(() => Template.SendBodyAsync("direct:a?block=false", "y"));
        Assert.Contains("No consumers", e.InnerException!.Message);

        gate.SetResult();
        await stop.Bounded();
        await send.Bounded();
    }

    [Fact]
    public async Task Timeout_forces_shutdown_by_cancelling_the_shutdown_token()
    {
        var time = new TestTimeProvider();
        Context.TimeProvider = time;
        Context.ShutdownStrategy.Timeout = TimeSpan.FromSeconds(10);
        var entered = NewSignal();
        await StartWithRoutes(b => b.From("direct:a").RouteId("stuck").Process(async e =>
        {
            entered.SetResult();
            await Task.Delay(Timeout.Infinite, e.CancellationToken);
        }));
        var send = Template.SendAsync("direct:a", e => e.Message.Body = "x");
        await entered.Task.Bounded();

        var timers = time.TimersCreated;
        var stop = Context.StopAsync();
        await time.WaitForTimersAsync(timers + 1);
        Assert.False(stop.IsCompleted);
        time.Advance(TimeSpan.FromSeconds(10));
        await stop.Bounded();

        Assert.True(Context.ShutdownStrategy.HasTimeoutOccurred);
        var exchange = await send.Bounded();
        Assert.IsAssignableFrom<OperationCanceledException>(exchange.Exception);
    }

    [Fact]
    public async Task Route_stop_timeout_cancels_seda_exchanges_but_not_the_context()
    {
        var time = new TestTimeProvider();
        Context.TimeProvider = time;
        Context.ShutdownStrategy.Timeout = TimeSpan.FromSeconds(5);
        var entered = NewSignal();
        var cancelled = NewSignal();
        await StartWithRoutes(b => b.From("seda:a").RouteId("stuck").Process(async e =>
        {
            entered.SetResult();
            try
            {
                await Task.Delay(Timeout.Infinite, e.CancellationToken);
            }
            finally
            {
                cancelled.SetResult();
            }
        }));
        await Template.SendBodyAsync("seda:a", "x");
        await entered.Task.Bounded();

        var timers = time.TimersCreated;
        var stop = Context.StopRouteAsync("stuck");
        await time.WaitForTimersAsync(timers + 1);
        time.Advance(TimeSpan.FromSeconds(5));
        await stop.Bounded();
        await cancelled.Task.Bounded();

        Assert.True(Context.ShutdownStrategy.HasTimeoutOccurred);
        Assert.False(Context.ShutdownToken.IsCancellationRequested);
        Assert.Equal(ServiceStatus.Stopped, Context.GetRouteStatus("stuck"));
    }

    [Fact]
    public async Task Complete_current_task_only_leaves_queued_messages()
    {
        var (gate, mock) = await StartSedaRouteWithQueuedMessages(ShutdownRunningTask.CompleteCurrentTaskOnly);

        var stop = Context.StopAsync();
        gate.SetResult();
        await stop.Bounded();

        Assert.Equal(1, mock.ReceivedCounter);
        Assert.Equal(4, Context.GetEndpoint<SedaEndpoint>("seda:work").CurrentQueueSize);
    }

    [Fact]
    public async Task Complete_all_tasks_drains_the_seda_queue()
    {
        var (gate, mock) = await StartSedaRouteWithQueuedMessages(ShutdownRunningTask.CompleteAllTasks);

        var stop = Context.StopAsync();
        gate.SetResult();
        await stop.Bounded();

        Assert.Equal(5, mock.ReceivedCounter);
        Assert.Equal(0, Context.GetEndpoint<SedaEndpoint>("seda:work").CurrentQueueSize);
    }

    [Fact]
    public async Task Deferred_route_keeps_consuming_until_the_others_are_done()
    {
        var (stop, send) = await StopWhileFeedingDirectRoute(ShutdownRoute.Defer);
        await stop.Bounded();
        await send.Bounded();

        Assert.Equal(1, Mock("out").ReceivedCounter);
    }

    [Fact]
    public async Task Without_defer_the_fed_route_stops_first()
    {
        var (stop, send) = await StopWhileFeedingDirectRoute(ShutdownRoute.Default);
        await stop.Bounded();

        var e = await Assert.ThrowsAsync<CaravanExecutionException>(() => send.Bounded());
        Assert.Contains("No consumers", e.InnerException!.Message);
    }

    [Fact]
    public async Task Default_strategy_has_camel_defaults()
    {
        var strategy = new DefaultShutdownStrategy();

        Assert.Equal(TimeSpan.FromSeconds(45), strategy.Timeout);
        Assert.True(strategy.LogInflightExchangesOnTimeout);
        Assert.IsType<DefaultShutdownStrategy>(Context.ShutdownStrategy);
        Assert.Equal(ShutdownRoute.Default, new Caravan.Model.RouteDefinition("direct:x").ShutdownRoute);
        Assert.Equal(ShutdownRunningTask.CompleteCurrentTaskOnly, new Caravan.Model.RouteDefinition("direct:x").ShutdownRunningTask);
        await Task.CompletedTask;
    }

    [Fact]
    public async Task Stopped_routes_raise_stop_events_in_reverse_startup_order()
    {
        var stopped = new List<string>();
        Context.EventNotifiers.Add(new CoreRoutingTests.RecordingNotifier(e =>
        {
            if (e.Type == CaravanEventType.RouteStopped)
            {
                stopped.Add(e.RouteId!);
            }
        }));
        await StartWithRoutes(b =>
        {
            b.From("direct:a").RouteId("a").StartupOrder(1).To("mock:x");
            b.From("direct:b").RouteId("b").StartupOrder(2).ShutdownRoute(ShutdownRoute.Defer).To("mock:x");
            b.From("direct:c").RouteId("c").StartupOrder(3).To("mock:x");
        });

        await Context.StopAsync();

        Assert.Equal(["c", "a", "b"], stopped);
    }

    private async Task<(TaskCompletionSource Gate, Caravan.Components.Mock.MockEndpoint Mock)> StartSedaRouteWithQueuedMessages(ShutdownRunningTask runningTask)
    {
        var entered = NewSignal();
        var gate = NewSignal();
        await StartWithRoutes(b => b.From("seda:work").ShutdownRunningTask(runningTask).Process(async _ =>
        {
            if (entered.TrySetResult())
            {
                await gate.Task;
            }
        }).To("mock:done"));
        for (var i = 0; i < 5; i++)
        {
            await Template.SendBodyAsync("seda:work", i);
        }

        await entered.Task.Bounded();
        return (gate, Mock("done"));
    }

    private async Task<(Task Stop, Task Send)> StopWhileFeedingDirectRoute(ShutdownRoute shutdownRoute)
    {
        var entered = NewSignal();
        var gate = NewSignal();
        await StartWithRoutes(b =>
        {
            // "out" starts last, so without Defer it is the first to stop.
            b.From("direct:in").RouteId("feeder").StartupOrder(1).Process(async _ =>
            {
                entered.SetResult();
                await gate.Task;
            }).To("direct:out?block=false");
            b.From("direct:out").RouteId("out").StartupOrder(2).ShutdownRoute(shutdownRoute).To("mock:out");
        });
        var send = Template.SendBodyAsync("direct:in", "x");
        await entered.Task.Bounded();

        var stop = Context.StopAsync();
        await WaitForConsumerStopped("feeder");
        gate.SetResult();
        return (stop, send);
    }

    private async Task WaitForConsumerStopped(string routeId)
    {
        var deadline = DateTime.UtcNow + Wait;
        while (Context.GetRoute(routeId)?.Consumer is { Status: not ServiceStatus.Stopped })
        {
            Assert.True(DateTime.UtcNow < deadline);
            await Task.Delay(1);
        }
    }
}
