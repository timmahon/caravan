using Caravan.Builder;
using Caravan.Observability;

namespace Caravan.Core.Tests.Observability;

public class MetricsTests : ContextTestBase
{
    private readonly MeasurementCollector _metrics;

    public MetricsTests()
    {
        Context.Name = "metrics-" + Guid.NewGuid().ToString("N");
        _metrics = new MeasurementCollector(Context.Name);
    }

    public override async Task DisposeAsync()
    {
        await base.DisposeAsync();
        _metrics.Dispose();
    }

    [Fact]
    public async Task Exchange_counters_and_durations_are_recorded_per_route()
    {
        await StartWithRoutes(b =>
        {
            b.From("direct:ok").RouteId("ok").To("direct:inner");
            b.From("direct:inner").RouteId("inner").Log("inner");
            b.From("direct:bad").RouteId("bad").Process(_ => throw new InvalidOperationException());
        });

        await Template.SendBodyAsync("direct:ok", 1);
        await Template.SendBodyAsync("direct:ok", 2);
        await Assert.ThrowsAsync<CaravanExecutionException>(() => Template.SendBodyAsync("direct:bad", 3));

        Assert.Equal(2, _metrics.Sum(CaravanSemanticConventions.ExchangesTotal, "ok"));
        Assert.Equal(2, _metrics.Sum(CaravanSemanticConventions.ExchangesTotal, "inner"));
        Assert.Equal(2, _metrics.Sum(CaravanSemanticConventions.ExchangesSucceeded, "ok"));
        Assert.Equal(0, _metrics.Sum(CaravanSemanticConventions.ExchangesFailed, "ok"));
        Assert.Equal(1, _metrics.Sum(CaravanSemanticConventions.ExchangesTotal, "bad"));
        Assert.Equal(1, _metrics.Sum(CaravanSemanticConventions.ExchangesFailed, "bad"));
        Assert.Equal(0, _metrics.Sum(CaravanSemanticConventions.ExchangesInflight));
        Assert.Equal(10, _metrics.Count(CaravanSemanticConventions.ExchangesInflight));
        Assert.Equal(2, _metrics.Count(CaravanSemanticConventions.ExchangeDuration, "ok"));
        Assert.All(
            _metrics.Measurements.Where(m => m.Name == CaravanSemanticConventions.ExchangeDuration),
            m => Assert.True(m.Value >= 0));
    }

    [Fact]
    public async Task Redeliveries_and_handled_failures_are_counted()
    {
        await StartWithRoutes(b =>
        {
            b.ErrorHandler(b.DeadLetterChannel("mock:dead").MaximumRedeliveries(2).RedeliveryDelay(0));
            b.From("direct:a").RouteId("a").Process(_ => throw new InvalidOperationException());
        });

        await Template.SendBodyAsync("direct:a", 1);

        Assert.Equal(2, _metrics.Sum(CaravanSemanticConventions.ExchangesRedeliveries, "a"));
        Assert.Equal(1, _metrics.Sum(CaravanSemanticConventions.ExchangesFailuresHandled, "a"));
        Assert.Equal(1, _metrics.Sum(CaravanSemanticConventions.ExchangesSucceeded, "a"));
    }

    [Fact]
    public async Task Send_durations_are_recorded_per_endpoint_without_secrets_or_options()
    {
        await StartWithRoutes(b => b.From("direct:a").RouteId("a").To("stub:x?password=p").To("mock:a"));

        await Template.SendBodyAsync("direct:a", 1);

        var endpoints = _metrics.Measurements.Where(m => m.Name == CaravanSemanticConventions.SendDuration)
            .Select(m => (string)m.Tags[CaravanSemanticConventions.EndpointUri]!)
            .ToList();
        Assert.Contains("direct:a", endpoints);
        Assert.Contains("stub:x", endpoints);
        Assert.Contains("mock:a", endpoints);
    }

    [Fact]
    public async Task Running_routes_are_observed()
    {
        await StartWithRoutes(b =>
        {
            b.From("direct:a").RouteId("a").To("mock:a");
            b.From("direct:b").RouteId("b").To("mock:b");
        });
        await Context.StopRouteAsync("b");

        _metrics.RecordObservable();

        var running = Assert.Single(_metrics.Measurements, m => m.Name == CaravanSemanticConventions.RoutesRunning);
        Assert.Equal(1, running.Value);
    }

    [Fact]
    public async Task Disabled_metrics_record_nothing()
    {
        Context.Telemetry.MetricsEnabled = false;
        await StartWithRoutes(b => b.From("direct:a").RouteId("a").To("mock:a"));

        await Template.SendBodyAsync("direct:a", 1);
        _metrics.RecordObservable();

        Assert.Empty(_metrics.Measurements);
    }
}
