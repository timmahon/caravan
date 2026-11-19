using Caravan.Builder;
using Caravan.Management;
using Caravan.Spi;

namespace Caravan.Core.Tests.Observability;

public class ManagementTests : ContextTestBase
{
    private readonly TestTimeProvider _time = new();

    public ManagementTests() => Context.TimeProvider = _time;

    [Fact]
    public async Task Route_and_node_statistics_count_exchanges_and_processing_times()
    {
        await StartWithRoutes(b => b.From("direct:a").RouteId("a")
            .Process(e => _time.Advance(TimeSpan.FromMilliseconds(e.Message.GetBody<int>()))).Id("work")
            .Filter(e => e.Message.GetBody<int>() > 15, f => f.Process(_ => throw new InvalidOperationException()).Id("fail")).Id("check"));

        await Template.SendBodyAsync("direct:a", 10);
        await Template.SendBodyAsync("direct:a", 30 - 20); // 10 again
        await Template.SendBodyAsync("direct:a", 5);
        await Assert.ThrowsAsync<CaravanExecutionException>(() => Template.SendBodyAsync("direct:a", 20));

        var route = Context.Management.GetRouteStatistics("a")!;
        Assert.Equal(ServiceStatus.Started, route.Status);
        Assert.Equal("direct:a", route.EndpointUri);
        var stats = route.Statistics;
        Assert.Equal(4, stats.ExchangesTotal);
        Assert.Equal(3, stats.ExchangesCompleted);
        Assert.Equal(1, stats.ExchangesFailed);
        Assert.Equal(0, stats.ExchangesInflight);
        Assert.Equal(TimeSpan.FromMilliseconds(5), stats.MinProcessingTime);
        Assert.Equal(TimeSpan.FromMilliseconds(10), stats.MaxProcessingTime);
        Assert.InRange(stats.MeanProcessingTime!.Value.TotalMilliseconds, 8.33, 8.34);
        Assert.Equal(TimeSpan.FromMilliseconds(5), stats.LastProcessingTime);
        Assert.Equal(TimeSpan.FromMilliseconds(25), stats.TotalProcessingTime);
        Assert.NotNull(stats.LastExchangeFailureTimestamp);
        Assert.NotNull(stats.FirstExchangeCompletedTimestamp);

        var nodes = Context.Management.GetProcessorStatistics("a");
        Assert.Equal(["work", "check", "fail"], nodes.Select(n => n.NodeId));
        Assert.Equal([1, 1, 2], nodes.Select(n => n.Level));
        Assert.Equal(4, nodes[0].Statistics.ExchangesCompleted);
        Assert.Equal(TimeSpan.FromMilliseconds(20), nodes[0].Statistics.MaxProcessingTime);
        Assert.Equal(3, nodes[1].Statistics.ExchangesCompleted);
        Assert.Equal(1, nodes[1].Statistics.ExchangesFailed);
        Assert.Equal(1, nodes[2].Statistics.ExchangesTotal);
        Assert.Equal(1, nodes[2].Statistics.ExchangesFailed);
        Assert.Equal("process", nodes[0].ShortName);
    }

    [Fact]
    public async Task Context_statistics_count_each_exchange_once()
    {
        await StartWithRoutes(b =>
        {
            b.From("direct:a").RouteId("a").To("direct:b");
            b.From("direct:b").RouteId("b").To("mock:b");
        });

        await Template.SendBodyAsync("direct:a", 1);
        await Template.SendBodyAsync("direct:b", 2);

        var context = Context.Management.GetContextStatistics();
        Assert.Equal(2, context.Statistics.ExchangesTotal);
        Assert.Equal(2, context.Statistics.ExchangesCompleted);
        Assert.Equal(2, context.Routes);
        Assert.Equal(2, context.StartedRoutes);
        Assert.Equal(ServiceStatus.Started, context.Status);
        Assert.Equal(2, Context.Management.GetRouteStatistics("b")!.Statistics.ExchangesTotal);
    }

    [Fact]
    public async Task Failures_handled_and_redeliveries_are_counted_on_route_and_node()
    {
        await StartWithRoutes(b =>
        {
            b.ErrorHandler(b.DeadLetterChannel("mock:dead").MaximumRedeliveries(3).RedeliveryDelay(0));
            b.From("direct:a").RouteId("a").Process(_ => throw new InvalidOperationException()).Id("fail");
        });

        await Template.SendBodyAsync("direct:a", 1);
        await Template.SendBodyAsync("direct:a", 2);

        var route = Context.Management.GetRouteStatistics("a")!.Statistics;
        Assert.Equal(6, route.Redeliveries);
        Assert.Equal(2, route.FailuresHandled);
        Assert.Equal(2, route.ExchangesCompleted);
        var node = Assert.Single(Context.Management.GetProcessorStatistics("a"), n => n.NodeId == "fail").Statistics;
        Assert.Equal(6, node.Redeliveries);
        Assert.Equal(2, node.FailuresHandled);
    }

    [Fact]
    public async Task Statistics_are_exact_under_parallel_load()
    {
        await StartWithRoutes(b =>
        {
            b.From("direct:a").RouteId("a").To("direct:b").Id("callB");
            b.From("direct:b").RouteId("b")
                .Process(async e => await Task.Yield()).Id("yield")
                .Filter(e => e.Message.GetBody<int>() % 10 == 0, f => f.Process(_ => throw new InvalidOperationException()));
        });

        await Parallel.ForEachAsync(
            Enumerable.Range(1, 400),
            new ParallelOptions { MaxDegreeOfParallelism = 16 },
            async (i, _) =>
            {
                await using var template = Context.CreateProducerTemplate();
                try
                {
                    await template.SendBodyAsync("direct:a", i);
                }
                catch (CaravanExecutionException)
                {
                }
            });

        foreach (var routeId in new[] { "a", "b" })
        {
            var stats = Context.Management.GetRouteStatistics(routeId)!.Statistics;
            Assert.Equal(400, stats.ExchangesTotal);
            Assert.Equal(360, stats.ExchangesCompleted);
            Assert.Equal(40, stats.ExchangesFailed);
            Assert.Equal(0, stats.ExchangesInflight);
        }

        var context = Context.Management.GetContextStatistics().Statistics;
        Assert.Equal(400, context.ExchangesTotal);
        Assert.Equal(40, context.ExchangesFailed);
        var yield = Assert.Single(Context.Management.GetProcessorStatistics("b"), n => n.NodeId == "yield").Statistics;
        Assert.Equal(400, yield.ExchangesCompleted);
        Assert.Equal(0, yield.ExchangesInflight);
        Assert.Equal(400, Context.Management.ListEndpoints().Single(e => e.Uri == "direct:b").InboundHits);
        Assert.Equal(400, Context.Management.ListEndpoints().Single(e => e.Uri == "direct:b").OutboundHits);
    }

    [Fact]
    public async Task Reset_clears_the_counters()
    {
        await StartWithRoutes(b => b.From("direct:a").RouteId("a").To("mock:a").Id("to"));
        await Template.SendBodyAsync("direct:a", 1);

        Context.Management.Reset();

        Assert.Equal(0, Context.Management.GetRouteStatistics("a")!.Statistics.ExchangesTotal);
        Assert.Null(Context.Management.GetRouteStatistics("a")!.Statistics.MinProcessingTime);
        Assert.NotNull(Context.Management.GetRouteStatistics("a")!.Statistics.ResetTimestamp);
        Assert.Equal(0, Context.Management.GetProcessorStatistics("a")[0].Statistics.ExchangesTotal);
        Assert.Equal(0, Context.Management.GetContextStatistics().Statistics.ExchangesTotal);
        Assert.Equal(0, ((Caravan.Impl.Route)Context.GetRoute("a")!).ExchangesTotal);

        await Template.SendBodyAsync("direct:a", 1);
        Assert.Equal(1, Context.Management.GetRouteStatistics("a")!.Statistics.ExchangesCompleted);
        Assert.False(Context.Management.ResetRoute("nope"));
        Assert.True(Context.Management.ResetRoute("a"));
        Assert.Equal(0, Context.Management.GetRouteStatistics("a")!.Statistics.ExchangesTotal);
    }

    [Fact]
    public async Task Statistics_level_routes_only_keeps_no_node_counters()
    {
        Context.Management.StatisticsLevel = ManagementStatisticsLevel.RoutesOnly;
        await StartWithRoutes(b => b.From("direct:a").RouteId("a").To("mock:a"));
        await Template.SendBodyAsync("direct:a", 1);

        Assert.Empty(Context.Management.GetProcessorStatistics("a"));
        Assert.Equal(1, Context.Management.GetRouteStatistics("a")!.Statistics.ExchangesTotal);
        Assert.Equal(1, Context.Management.GetContextStatistics().Statistics.ExchangesTotal);
    }

    [Fact]
    public async Task Uptime_follows_the_route_lifecycle()
    {
        await StartWithRoutes(b => b.From("direct:a").RouteId("a").To("mock:a"));
        _time.Advance(TimeSpan.FromMinutes(2));

        Assert.Equal(TimeSpan.FromMinutes(2), Context.Management.GetRouteStatistics("a")!.Uptime);
        Assert.Equal(TimeSpan.FromMinutes(2), Context.Management.GetContextStatistics().Uptime);

        await Context.Management.StopRouteAsync("a");
        Assert.Equal(ServiceStatus.Stopped, Context.Management.GetRouteStatistics("a")!.Status);
        Assert.Equal(TimeSpan.Zero, Context.Management.GetRouteStatistics("a")!.Uptime);

        await Context.Management.StartRouteAsync("a");
        _time.Advance(TimeSpan.FromSeconds(3));
        Assert.Equal(TimeSpan.FromSeconds(3), Context.Management.GetRouteStatistics("a")!.Uptime);

        await Context.Management.SuspendRouteAsync("a");
        Assert.Equal(ServiceStatus.Suspended, Context.GetRouteStatus("a"));
        await Context.Management.ResumeRouteAsync("a");
        Assert.Equal(ServiceStatus.Started, Context.GetRouteStatus("a"));
    }

    [Fact]
    public async Task Routes_are_dumped_as_text()
    {
        await StartWithRoutes(b => b.From("direct:a?timeout=5").RouteId("a").RouteGroup("g")
            .Choice(c => c.When(Pred.Constant(true), w => w.To("mock:x").Id("toX")).Otherwise(o => o.Log("other").Id("log"))).Id("choice"));

        var text = Context.Management.DumpRouteAsText("a")!;

        Assert.Equal(
            """
            route (id=a) group=g
              from[direct:a?timeout=5]
              choice (id=choice)
            """.ReplaceLineEndings(),
            string.Join(Environment.NewLine, text.Split(Environment.NewLine).Take(3)));
        Assert.Contains("    to[mock:x] (id=toX)", text, StringComparison.Ordinal);
        Assert.Null(Context.Management.DumpRouteAsText("nope"));
    }

    [Fact]
    public async Task Endpoints_components_circuit_breakers_and_inflight_are_listed()
    {
        var gate = TestHelpers.NewSignal();
        var entered = TestHelpers.NewSignal();
        await StartWithRoutes(b =>
        {
            b.From("seda:in").RouteId("slow").Process(async _ =>
            {
                entered.TrySetResult();
                await gate.Task;
            });
            b.From("direct:cb").RouteId("cb").CircuitBreaker(c => c.To("mock:cb")).Id("breaker");
            b.From("direct:secret").RouteId("secret").To("stub:x?password=p");
        });
        await Template.SendBodyAsync("direct:cb", 1);
        await Template.SendBodyAsync("seda:in", 1);
        await entered.Task.Bounded();
        try
        {
            AssertListings();
        }
        finally
        {
            gate.TrySetResult();
        }
    }

    private void AssertListings()
    {
        var inflight = Assert.Single(Context.Management.BrowseInflight("slow"));
        Assert.Equal("slow", inflight.RouteId);
        Assert.Empty(Context.Management.BrowseInflight("cb"));

        var endpoints = Context.Management.ListEndpoints();
        var seda = Assert.Single(endpoints, e => e.Uri == "seda:in");
        Assert.Equal(["slow"], seda.ConsumingRoutes);
        Assert.Equal(1, seda.InboundHits);
        Assert.Equal(1, seda.OutboundHits);
        Assert.Contains(endpoints, e => e.Uri == "stub:x?password=xxxxxx");
        Assert.Equal("seda", seda.Scheme);

        var components = Context.Management.ListComponents();
        Assert.Contains(components, c => c.Scheme == "seda" && c.Status == ServiceStatus.Started && c.Type!.EndsWith("SedaComponent", StringComparison.Ordinal));
        Assert.Contains(components, c => c.Scheme == "timer" && c.Status is null);

        var breaker = Assert.Single(Context.Management.ListCircuitBreakers());
        Assert.Equal(new CircuitBreakerInfo("breaker", CircuitBreakerState.Closed), breaker);
    }
}
