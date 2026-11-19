using Caravan.Builder;
using Caravan.Components.Direct;
using Caravan.Components.Seda;
using Caravan.Model;
using Caravan.RoutePolicies;
using Caravan.Spi;

namespace Caravan.Core.Tests;

/// <summary>Route policies: the callbacks, <c>routePolicyRef</c>, factories and the throttling policies (Camel's RoutePolicy tests).</summary>
public class RoutePolicyTests : ContextTestBase
{
    [Fact]
    public async Task Policies_are_called_on_the_route_lifecycle_and_every_exchange()
    {
        var policy = new RecordingPolicy();
        await StartWithRoutes(b => b.From("direct:start").RouteId("r").RoutePolicy(policy).To("mock:result"));

        await Template.SendBodyAsync("direct:start", "x");
        await Context.SuspendRouteAsync("r");
        await Context.ResumeRouteAsync("r");
        await Context.StopRouteAsync("r");
        await Context.RemoveRouteAsync("r");

        Assert.Equal(["init r", "start r", "begin x", "done x", "suspend r", "resume r", "stop r", "remove r"], policy.Events);
    }

    [Fact]
    public async Task Exchange_done_sees_the_outcome_and_failing_policies_are_ignored()
    {
        var policy = new RecordingPolicy();
        var failing = new FailingPolicy();
        await StartWithRoutes(b => b.From("direct:start").RoutePolicy(failing, policy).Process(e =>
        {
            if (e.Message.Body is "bad")
            {
                throw new InvalidOperationException("boom");
            }
        }).To("mock:result"));

        await Template.SendBodyAsync("direct:start", "ok");
        var failed = await Template.SendAsync("direct:start", e => e.Message.Body = "bad");

        Assert.IsType<InvalidOperationException>(failed.Exception);
        Assert.Equal(["ok"], Mock("result").ReceivedBodies);
        Assert.Contains("done ok", policy.Events);
        Assert.Contains("done bad failed", policy.Events);
    }

    [Fact]
    public async Task Policies_come_from_registry_references_and_factories()
    {
        var byRef = new RecordingPolicy();
        Context.Registry.Bind("myPolicy", byRef);
        var factory = new Factory();
        Context.RoutePolicyFactories.Add(factory);
        await StartWithRoutes(b =>
        {
            b.From("direct:a").RouteId("a").RoutePolicyRef("myPolicy").To("mock:a");
            b.From("direct:b").RouteId("b").To("mock:b");
        });

        await Template.SendBodyAsync("direct:a", "1");
        await Template.SendBodyAsync("direct:b", "2");

        Assert.Equal(["init a", "start a", "begin 1", "done 1"], byRef.Events);
        Assert.Equal(["a", "b"], factory.Created.Keys.Order());
        Assert.Contains("done 2", factory.Created["b"].Events);
    }

    [Fact]
    public async Task A_missing_policy_reference_fails_route_creation()
    {
        Context.AddRoutes(RouteBuilder.Create(b => b.From("direct:a").RoutePolicyRef("nope").To("mock:a")));

        var e = await Assert.ThrowsAsync<FailedToCreateRouteException>(() => Context.StartAsync());
        Assert.Contains("nope", e.Message);
    }

    [Fact]
    public async Task Inflight_policy_suspends_the_consumer_above_the_maximum_and_resumes_it_below_the_threshold()
    {
        var gates = new Dictionary<string, TaskCompletionSource> { ["a"] = TestHelpers.NewSignal(), ["b"] = TestHelpers.NewSignal(), ["c"] = TestHelpers.NewSignal() };
        var policy = new ThrottlingInflightRoutePolicy { MaxInflightExchanges = 1, ResumePercentOfMax = 0 };
        await StartWithRoutes(b => b.From("seda:in?concurrentConsumers=3").RouteId("r").RoutePolicy(policy)
            .Process(async e =>
            {
                if (gates.TryGetValue((string)e.Message.Body!, out var gate))
                {
                    await gate.Task;
                }
            })
            .To("mock:result"));
        var route = Context.GetRoute("r")!;

        foreach (var body in new[] { "a", "b", "c" })
        {
            await Template.SendBodyAsync("seda:in", body);
        }

        await TestHelpers.WaitUntilAsync(() => route.InflightCount == 3);
        gates["a"].SetResult();
        await TestHelpers.WaitUntilAsync(() => route.Consumer is IService { Status: ServiceStatus.Suspending or ServiceStatus.Suspended });

        await Template.SendBodyAsync("seda:in", "d");
        var endpoint = Context.GetEndpoint<SedaEndpoint>("seda:in");
        Assert.Equal(1, endpoint.CurrentQueueSize);
        Assert.Equal(ServiceStatus.Started, route.Status);

        gates["b"].SetResult();
        gates["c"].SetResult();
        Mock("result").ExpectedBodiesReceivedInAnyOrder("a", "b", "c", "d");
        await Mock("result").AssertIsSatisfiedAsync();
        await policy.PendingOperation.Bounded();
        Assert.Equal(ServiceStatus.Started, ((IService)route.Consumer!).Status);
        Assert.Equal("d", Mock("result").ReceivedBodies[3]);
        Assert.Equal(0, policy.ResumeInflightExchanges);
    }

    [Fact]
    public async Task Inflight_policy_with_context_scope_counts_every_route()
    {
        var policy = new ThrottlingInflightRoutePolicy { MaxInflightExchanges = 0, Scope = ThrottlingScope.Context };
        await StartWithRoutes(b => b.From("direct:start").RoutePolicy(policy).To("mock:result"));

        await Template.SendBodyAsync("direct:start", "x");

        Assert.Equal(["x"], Mock("result").ReceivedBodies);
        Assert.Contains("using scope Context", policy.ToString());
    }

    [Fact]
    public async Task Exception_policy_opens_the_circuit_after_the_threshold_and_half_opens_after_the_delay()
    {
        var time = new TestTimeProvider();
        Context.TimeProvider = time;
        var policy = new ThrottlingExceptionRoutePolicy
        {
            FailureThreshold = 2,
            FailureWindow = TimeSpan.FromMinutes(1),
            HalfOpenAfter = TimeSpan.FromSeconds(10),
        };
        await StartWithRoutes(b => b.From("direct:start?block=false").RouteId("r").RoutePolicy(policy)
            .ErrorHandler(new NoErrorHandlerDefinition())
            .Process(e =>
            {
                if (e.Message.Body is "bad")
                {
                    throw new IOException("down");
                }
            })
            .To("mock:result"));

        await Template.SendAsync("direct:start?block=false", e => e.Message.Body = "bad");
        Assert.Equal(ThrottlingExceptionState.Closed, policy.State);
        await Template.SendAsync("direct:start?block=false", e => e.Message.Body = "bad");
        Assert.Equal(ThrottlingExceptionState.Open, policy.State);
        await policy.PendingOperation.Bounded();

        var rejected = await Template.SendAsync("direct:start?block=false", e => e.Message.Body = "ok");
        Assert.IsType<DirectConsumerNotAvailableException>(rejected.Exception);

        time.Advance(TimeSpan.FromSeconds(10));
        Assert.Equal(ThrottlingExceptionState.HalfOpen, policy.State);
        await policy.PendingOperation.Bounded();

        // A failure while half-open opens the circuit again.
        await Template.SendAsync("direct:start?block=false", e => e.Message.Body = "bad");
        Assert.Equal(ThrottlingExceptionState.Open, policy.State);
        await policy.PendingOperation.Bounded();
        time.Advance(TimeSpan.FromSeconds(10));
        await policy.PendingOperation.Bounded();

        // A success while half-open closes it.
        await Template.SendBodyAsync("direct:start?block=false", "ok");
        Assert.Equal(ThrottlingExceptionState.Closed, policy.State);
        Assert.Equal(0, policy.Failures);
        Assert.Equal(["ok"], Mock("result").ReceivedBodies);
    }

    [Fact]
    public async Task Exception_policy_counts_only_throttled_exceptions_within_the_window()
    {
        var time = new TestTimeProvider();
        Context.TimeProvider = time;
        var policy = new ThrottlingExceptionRoutePolicy { FailureThreshold = 2, FailureWindow = TimeSpan.FromSeconds(30) };
        policy.ThrottledExceptions.Add(typeof(IOException));
        await StartWithRoutes(b => b.From("direct:start").RoutePolicy(policy).ErrorHandler(new NoErrorHandlerDefinition())
            .Process(e => throw (e.Message.Body is "io" ? new FileNotFoundException("x") : new InvalidOperationException("y"))));

        await Template.SendAsync("direct:start", e => e.Message.Body = "other");
        await Template.SendAsync("direct:start", e => e.Message.Body = "other");
        Assert.Equal(0, policy.Failures);

        await Template.SendAsync("direct:start", e => e.Message.Body = "io");
        time.Advance(TimeSpan.FromSeconds(31));
        await Template.SendAsync("direct:start", e => e.Message.Body = "io");

        Assert.Equal(1, policy.Failures);
        Assert.Equal(ThrottlingExceptionState.Closed, policy.State);
    }

    [Fact]
    public async Task Exception_policy_stays_open_while_the_half_open_handler_refuses_and_with_keep_open()
    {
        var time = new TestTimeProvider();
        Context.TimeProvider = time;
        var handler = new Handler();
        var policy = new ThrottlingExceptionRoutePolicy { FailureThreshold = 1, HalfOpenAfter = TimeSpan.FromSeconds(5), HalfOpenHandler = handler };
        await StartWithRoutes(b => b.From("direct:start?block=false").RoutePolicy(policy).ErrorHandler(new NoErrorHandlerDefinition())
            .Process(e => throw new IOException("down")));

        await Template.SendAsync("direct:start?block=false", _ => { });
        Assert.Equal(ThrottlingExceptionState.Open, policy.State);
        time.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal(ThrottlingExceptionState.Open, policy.State);
        Assert.Equal(1, handler.Calls);

        handler.Ready = true;
        policy.KeepOpen = true;
        time.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal(ThrottlingExceptionState.Open, policy.State);

        policy.KeepOpen = false;
        time.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal(ThrottlingExceptionState.HalfOpen, policy.State);
        await policy.PendingOperation.Bounded();
        policy.Dispose();
    }

    private sealed class RecordingPolicy : RoutePolicySupport
    {
        public List<string> Events { get; } = [];

        public override void OnInit(IRoute route) => Add("init " + route.Id);

        public override void OnStart(IRoute route) => Add("start " + route.Id);

        public override void OnStop(IRoute route) => Add("stop " + route.Id);

        public override void OnSuspend(IRoute route) => Add("suspend " + route.Id);

        public override void OnResume(IRoute route) => Add("resume " + route.Id);

        public override void OnRemove(IRoute route) => Add("remove " + route.Id);

        public override void OnExchangeBegin(IRoute route, Exchange exchange) => Add("begin " + exchange.Message.Body);

        public override void OnExchangeDone(IRoute route, Exchange exchange) =>
            Add("done " + exchange.Message.Body + (exchange.IsFailed ? " failed" : string.Empty));

        private void Add(string text)
        {
            lock (Events)
            {
                Events.Add(text);
            }
        }
    }

    private sealed class FailingPolicy : IRoutePolicy
    {
        public void OnExchangeBegin(IRoute route, Exchange exchange) => throw new InvalidOperationException("policy bug");

        public void OnExchangeDone(IRoute route, Exchange exchange) => throw new InvalidOperationException("policy bug");
    }

    private sealed class Factory : IRoutePolicyFactory
    {
        public Dictionary<string, RecordingPolicy> Created { get; } = [];

        public IRoutePolicy? CreateRoutePolicy(ICaravanContext context, string routeId, RouteDefinition route) => Created[routeId] = new RecordingPolicy();
    }

    private sealed class Handler : IThrottlingExceptionHalfOpenHandler
    {
        public bool Ready { get; set; }

        public int Calls { get; private set; }

        public bool IsReadyToBeClosed()
        {
            Calls++;
            return Ready;
        }
    }
}
