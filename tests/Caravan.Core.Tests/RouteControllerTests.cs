using Caravan.Builder;
using Caravan.Components.Direct;
using Caravan.Impl;
using Caravan.Spi;
using Caravan.Support;

namespace Caravan.Core.Tests;

public class RouteControllerTests : ContextTestBase
{
    private readonly List<CaravanEvent> _events = [];

    public RouteControllerTests()
    {
        Context.EventNotifiers.Add(new CoreRoutingTests.RecordingNotifier(e =>
        {
            lock (_events)
            {
                _events.Add(e);
            }
        }));
    }

    private List<CaravanEventType> RouteEvents(string routeId)
    {
        lock (_events)
        {
            return _events.Where(e => e.RouteId == routeId && e.Exchange is null).Select(e => e.Type).ToList();
        }
    }

    [Fact]
    public async Task Suspend_and_resume_raise_events_and_change_status()
    {
        await StartWithRoutes(b => b.From("direct:a").RouteId("r").To("mock:result"));

        await Context.SuspendRouteAsync("r");
        Assert.Equal(ServiceStatus.Suspended, Context.GetRouteStatus("r"));
        await Context.ResumeRouteAsync("r");
        Assert.Equal(ServiceStatus.Started, Context.GetRouteStatus("r"));

        Assert.Equal(
            [CaravanEventType.RouteAdded, CaravanEventType.RouteStarted, CaravanEventType.RouteSuspended, CaravanEventType.RouteResumed],
            RouteEvents("r"));
    }

    [Fact]
    public async Task Suspended_direct_route_does_not_consume_until_resumed()
    {
        await StartWithRoutes(b => b.From("direct:a").RouteId("r").To("mock:result"));
        await Context.SuspendRouteAsync("r");

        var e = await Assert.ThrowsAsync<CaravanExecutionException>(() => Template.SendBodyAsync("direct:a?block=false", "x"));
        Assert.IsType<DirectConsumerNotAvailableException>(e.InnerException);
        Assert.NotNull(Context.GetRoute("r"));

        await Context.ResumeRouteAsync("r");
        Mock("result").ExpectedBodiesReceived("y");
        await Template.SendBodyAsync("direct:a", "y");
        await Mock("result").AssertIsSatisfiedAsync();
    }

    [Fact]
    public async Task Suspending_a_stopped_route_does_nothing_and_resume_starts_it()
    {
        await StartWithRoutes(b => b.From("direct:a").RouteId("r").AutoStartup(false).To("mock:result"));
        Assert.Equal(ServiceStatus.Initialized, Context.GetRouteStatus("r"));

        await Context.SuspendRouteAsync("r");
        Assert.Equal(ServiceStatus.Initialized, Context.GetRouteStatus("r"));

        await Context.ResumeRouteAsync("r");
        Assert.Equal(ServiceStatus.Started, Context.GetRouteStatus("r"));
    }

    [Fact]
    public async Task Start_route_resumes_a_suspended_route()
    {
        await StartWithRoutes(b => b.From("seda:a").RouteId("r").To("mock:result"));
        await Context.SuspendRouteAsync("r");

        await Context.StartRouteAsync("r");

        Assert.Equal(ServiceStatus.Started, Context.GetRouteStatus("r"));
        Mock("result").ExpectedBodiesReceived("x");
        await Template.SendBodyAsync("seda:a", "x");
        await Mock("result").AssertIsSatisfiedAsync();
    }

    [Fact]
    public async Task Suspended_route_can_be_stopped_and_removed()
    {
        await StartWithRoutes(b => b.From("seda:a").RouteId("r").To("mock:result"));
        await Context.SuspendRouteAsync("r");

        await Context.StopRouteAsync("r");

        Assert.Equal(ServiceStatus.Stopped, Context.GetRouteStatus("r"));
        Assert.True(await Context.RemoveRouteAsync("r"));
        Assert.Null(Context.GetRouteStatus("r"));
    }

    [Fact]
    public async Task Unknown_routes_are_rejected()
    {
        await Context.StartAsync();

        await Assert.ThrowsAsync<ArgumentException>(() => Context.SuspendRouteAsync("nope"));
        await Assert.ThrowsAsync<ArgumentException>(() => Context.ResumeRouteAsync("nope"));
        Assert.Null(Context.GetRouteStatus("nope"));
    }

    [Fact]
    public async Task Without_supervision_a_failing_route_fails_the_context()
    {
        Context.AddComponent("flaky", new FlakyComponent(failures: 1));

        await Assert.ThrowsAsync<FailedToStartRouteException>(() => StartWithRoutes(b => b.From("flaky:x").RouteId("r").To("mock:result")));
    }

    [Fact]
    public async Task Supervising_controller_retries_failed_routes_with_backoff()
    {
        var time = new TestTimeProvider();
        Context.TimeProvider = time;
        var flaky = new FlakyComponent(failures: 3);
        Context.AddComponent("flaky", flaky);
        var supervisor = new SupervisingRouteController { BackOffDelay = TimeSpan.FromSeconds(1), BackOffMultiplier = 2 };
        Context.SupervisingRouteController = supervisor;

        await StartWithRoutes(b =>
        {
            b.From("flaky:x").RouteId("flaky").To("mock:result");
            b.From("direct:ok").RouteId("ok").To("mock:result");
        });
        await time.WaitForTimersAsync(1);

        Assert.Equal(ServiceStatus.Started, Context.Status);
        Assert.Equal(ServiceStatus.Started, Context.GetRouteStatus("ok"));
        Assert.Equal(ServiceStatus.Failed, Context.GetRouteStatus("flaky"));
        var state = supervisor.GetRestartState("flaky")!;
        Assert.Equal(0, state.Attempts);
        Assert.Equal("flaky start failure 1", state.LastError!.GetBaseException().Message);
        Assert.Equal(time.GetUtcNow().AddSeconds(1), state.NextAttempt);

        await time.AdvanceAndWaitForTimerAsync(TimeSpan.FromSeconds(1));
        Assert.Equal(1, state.Attempts);
        Assert.Equal(time.GetUtcNow().AddSeconds(2), state.NextAttempt);

        await time.AdvanceAndWaitForTimerAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(2, state.Attempts);

        time.Advance(TimeSpan.FromSeconds(4));
        await WaitUntil(() => Context.GetRouteStatus("flaky") == ServiceStatus.Started);
        Assert.Null(supervisor.GetRestartState("flaky"));
        Assert.Empty(supervisor.FailedRoutes);
        Assert.Equal(4, flaky.Starts);

        var restarting = RouteEvents("flaky");
        Assert.Equal(3, restarting.Count(t => t == CaravanEventType.RouteRestarting));
        Assert.Equal(2, restarting.Count(t => t == CaravanEventType.RouteRestartingFailure));
        Assert.Equal(CaravanEventType.RouteStarted, restarting[^1]);
    }

    [Fact]
    public async Task Supervising_controller_gives_up_after_max_attempts()
    {
        var time = new TestTimeProvider();
        Context.TimeProvider = time;
        Context.AddComponent("flaky", new FlakyComponent(failures: 100));
        var supervisor = new SupervisingRouteController { BackOffDelay = TimeSpan.FromSeconds(1), BackOffMaxAttempts = 2 };
        Context.SupervisingRouteController = supervisor;

        await StartWithRoutes(b => b.From("flaky:x").RouteId("flaky").To("mock:result"));
        await time.WaitForTimersAsync(1);
        await time.AdvanceAndWaitForTimerAsync(TimeSpan.FromSeconds(1));
        time.Advance(TimeSpan.FromSeconds(1));

        await WaitUntil(() => supervisor.GetRestartState("flaky")?.IsExhausted == true);
        Assert.Equal(["flaky"], supervisor.ExhaustedRouteIds);
        Assert.Equal(2, supervisor.GetRestartState("flaky")!.Attempts);
        Assert.Equal(ServiceStatus.Failed, Context.GetRouteStatus("flaky"));
    }

    [Fact]
    public async Task Supervising_controller_initial_delay_starts_routes_later()
    {
        var time = new TestTimeProvider();
        Context.TimeProvider = time;
        Context.SupervisingRouteController = new SupervisingRouteController { InitialDelay = TimeSpan.FromSeconds(5) };

        await StartWithRoutes(b => b.From("direct:a").RouteId("r").To("mock:result"));
        await time.WaitForTimersAsync(1);
        Assert.Equal(ServiceStatus.Initialized, Context.GetRouteStatus("r"));

        time.Advance(TimeSpan.FromSeconds(5));

        await WaitUntil(() => Context.GetRouteStatus("r") == ServiceStatus.Started);
    }

    [Fact]
    public async Task Stopping_the_context_cancels_supervision()
    {
        var time = new TestTimeProvider();
        Context.TimeProvider = time;
        var flaky = new FlakyComponent(failures: 100);
        Context.AddComponent("flaky", flaky);
        Context.SupervisingRouteController = new SupervisingRouteController { BackOffDelay = TimeSpan.FromSeconds(1) };
        await StartWithRoutes(b => b.From("flaky:x").RouteId("flaky").To("mock:result"));
        await time.WaitForTimersAsync(1);

        await Context.StopAsync().Bounded();
        time.Advance(TimeSpan.FromSeconds(10));

        Assert.Equal(1, flaky.Starts);
        Assert.Empty(Context.SupervisingRouteController.FailedRoutes);
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TestHelpers.Wait;
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "condition not met in time");
            await Task.Delay(1);
        }
    }

    private sealed class FlakyComponent(int failures) : ComponentBase
    {
        private int _starts;

        public int Starts => Volatile.Read(ref _starts);

        public bool NextStartFails() => Interlocked.Increment(ref _starts) <= failures;

        protected override EndpointBase CreateEndpoint(string uri, string remaining, IDictionary<string, string> parameters) =>
            new FlakyEndpoint(uri, this);

        private sealed class FlakyEndpoint(string uri, FlakyComponent component) : EndpointBase(uri, component, component.Context!)
        {
            public override IProducer CreateProducer() => throw new NotSupportedException();

            public override IConsumer CreateConsumer(IProcessor processor) => new FlakyConsumer(this, processor, component);
        }

        private sealed class FlakyConsumer(IEndpoint endpoint, IProcessor processor, FlakyComponent component) : ConsumerBase(endpoint, processor)
        {
            protected override Task DoStartAsync(CancellationToken cancellationToken) =>
                component.NextStartFails()
                    ? throw new InvalidOperationException($"flaky start failure {component.Starts}")
                    : Task.CompletedTask;
        }
    }
}
