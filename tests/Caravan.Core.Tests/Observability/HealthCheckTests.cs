using Caravan.Builder;
using Caravan.Health;
using Caravan.Impl;
using Caravan.Spi;
using Caravan.Support;

namespace Caravan.Core.Tests.Observability;

public class HealthCheckTests : ContextTestBase
{
    private async Task<HealthCheckResult> Check(string id) =>
        Assert.Single(await Context.HealthChecks.CheckAsync(c => c.Id == id));

    [Fact]
    public async Task The_context_check_is_up_only_while_started()
    {
        Assert.Equal(HealthState.Down, (await Check("context")).State);

        await StartWithRoutes(b => b.From("direct:a").RouteId("a").To("mock:a"));
        var result = await Check("context");
        Assert.Equal(HealthState.Up, result.State);
        Assert.Equal("context", result.Group);

        await Context.StopAsync();
        Assert.Equal(HealthState.Down, (await Check("context")).State);
        Assert.Single(await Context.HealthChecks.CheckLivenessAsync());
    }

    [Fact]
    public async Task Route_checks_report_stopped_and_intentionally_stopped_routes()
    {
        await StartWithRoutes(b =>
        {
            b.From("direct:a").RouteId("a").To("mock:a");
            b.From("direct:b").RouteId("b").AutoStartup(false).To("mock:b");
        });

        Assert.Equal(HealthState.Up, (await Check("route:a")).State);
        Assert.Equal(HealthState.Up, (await Check("route:b")).State);

        await Context.StopRouteAsync("a");
        var stopped = await Check("route:a");
        Assert.Equal(HealthState.Degraded, stopped.State);
        Assert.Equal("Stopped", stopped.Details["route.status"]);

        var readiness = await Context.HealthChecks.CheckReadinessAsync();
        Assert.Equal(HealthState.Degraded, HealthCheckRegistry.Aggregate(readiness));
    }

    [Fact]
    public async Task Supervised_routes_are_unknown_while_retried_and_down_once_exhausted()
    {
        var time = new TestTimeProvider();
        Context.TimeProvider = time;
        var supervisor = new SupervisingRouteController { BackOffDelay = TimeSpan.FromSeconds(1), BackOffMaxAttempts = 1 };
        Context.SupervisingRouteController = supervisor;
        Context.AddComponent("broken", new BrokenComponent());

        await StartWithRoutes(b => b.From("broken:x").RouteId("broken").To("mock:a"));
        await time.WaitForTimersAsync(1);

        Assert.Equal(HealthState.Unknown, (await Check("route:broken")).State);
        Assert.Equal(HealthState.Unknown, (await Check("consumer:broken")).State);

        time.Advance(TimeSpan.FromSeconds(1));
        await TestHelpers.WaitUntilAsync(() => supervisor.GetRestartState("broken")?.IsExhausted == true);

        var route = await Check("route:broken");
        Assert.Equal(HealthState.Down, route.State);
        var consumer = await Check("consumer:broken");
        Assert.Equal(HealthState.Down, consumer.State);
        Assert.Contains("cannot start", consumer.Error!.Message, StringComparison.Ordinal);
        Assert.Equal(HealthState.Down, HealthCheckRegistry.Aggregate(await Context.HealthChecks.CheckReadinessAsync()));
    }

    [Fact]
    public async Task Open_circuit_breakers_degrade_readiness()
    {
        await StartWithRoutes(b => b.From("direct:a").RouteId("a").CircuitBreaker(c => c.To("mock:a")).Id("cb"));
        await Template.SendBodyAsync("direct:a", 1);
        Assert.Equal(HealthState.Up, (await Check("circuit-breakers")).State);

        await Context.GetCircuitBreakerStateRegistry().IsolateAsync("cb");

        var result = await Check("circuit-breakers");
        Assert.Equal(HealthState.Degraded, result.State);
        Assert.Equal("Isolated", result.Details["cb"]);
    }

    [Fact]
    public async Task Full_seda_queues_are_reported_by_the_component()
    {
        await StartWithRoutes(b => b.From("direct:a").RouteId("a").To("mock:a"));
        await Template.SendBodyAsync("seda:q?size=2", 1);
        Assert.Equal(HealthState.Up, (await Check("seda:q")).State);

        await Template.SendBodyAsync("seda:q?size=2", 2);

        var result = await Check("seda:q");
        Assert.Equal(HealthState.Degraded, result.State);
        Assert.Equal("components", result.Group);
        Assert.Equal(2, result.Details["queue.size"]);
    }

    [Fact]
    public async Task Checks_and_repositories_come_from_the_registry_and_failures_are_down()
    {
        Context.Registry.Bind("custom", new DelegateCheck("custom", _ => throw new InvalidOperationException("db down")));
        Context.HealthChecks.Register(new DelegateCheck("ok", id => new HealthCheckResult(id, HealthState.Up)));
        await StartWithRoutes(b => b.From("direct:a").RouteId("a").To("mock:a"));

        var custom = await Check("custom");
        Assert.Equal(HealthState.Down, custom.State);
        Assert.Equal("db down", custom.Message);
        Assert.Equal(HealthState.Up, (await Check("ok")).State);

        Context.HealthChecks.ExcludePattern = "custom,consumers";
        var ids = (await Context.HealthChecks.CheckAsync()).Select(r => r.Id).ToList();
        Assert.DoesNotContain("custom", ids);
        Assert.DoesNotContain("consumer:a", ids);
        Assert.Contains("route:a", ids);

        Context.HealthChecks.Enabled = false;
        Assert.Empty(await Context.HealthChecks.CheckAsync());
    }

    private sealed class DelegateCheck(string id, Func<string, HealthCheckResult> check) : IHealthCheck
    {
        public string Id => id;

        public string Group => "custom";

        public ValueTask<HealthCheckResult> CheckAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(check(id));
    }

    private sealed class BrokenComponent : ComponentBase
    {
        protected override EndpointBase CreateEndpoint(string uri, string remaining, IDictionary<string, string> parameters) => new BrokenEndpoint(uri, this);

        private sealed class BrokenEndpoint(string uri, BrokenComponent component) : EndpointBase(uri, component, component.Context!)
        {
            public override IProducer CreateProducer() => throw new NotSupportedException();

            public override IConsumer CreateConsumer(IProcessor processor) => new BrokenConsumer(this, processor);
        }

        private sealed class BrokenConsumer(IEndpoint endpoint, IProcessor processor) : ConsumerBase(endpoint, processor)
        {
            protected override Task DoStartAsync(CancellationToken cancellationToken) => throw new InvalidOperationException("cannot start");
        }
    }
}
