using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Caravan.Spi;

namespace Caravan.Observability;

/// <summary>The instruments of <see cref="CaravanDiagnostics.Meter"/>. Every recording first checks that the instrument has a listener.</summary>
internal static class CaravanMetrics
{
    private static readonly ConcurrentDictionary<ICaravanContext, byte> Contexts = new(ReferenceEqualityComparer.Instance);

    public static readonly Counter<long> ExchangesTotal = CaravanDiagnostics.Meter.CreateCounter<long>(
        CaravanSemanticConventions.ExchangesTotal, "{exchange}", "Exchanges that entered a route");

    public static readonly Counter<long> ExchangesSucceeded = CaravanDiagnostics.Meter.CreateCounter<long>(
        CaravanSemanticConventions.ExchangesSucceeded, "{exchange}", "Exchanges that left a route successfully");

    public static readonly Counter<long> ExchangesFailed = CaravanDiagnostics.Meter.CreateCounter<long>(
        CaravanSemanticConventions.ExchangesFailed, "{exchange}", "Exchanges that left a route failed");

    public static readonly Counter<long> FailuresHandled = CaravanDiagnostics.Meter.CreateCounter<long>(
        CaravanSemanticConventions.ExchangesFailuresHandled, "{exchange}", "Failures handled by an error handler");

    public static readonly Counter<long> Redeliveries = CaravanDiagnostics.Meter.CreateCounter<long>(
        CaravanSemanticConventions.ExchangesRedeliveries, "{attempt}", "Redelivery attempts of error handlers");

    public static readonly UpDownCounter<long> Inflight = CaravanDiagnostics.Meter.CreateUpDownCounter<long>(
        CaravanSemanticConventions.ExchangesInflight, "{exchange}", "Exchanges currently in a route");

    public static readonly Histogram<double> ExchangeDuration = CaravanDiagnostics.Meter.CreateHistogram<double>(
        CaravanSemanticConventions.ExchangeDuration, "s", "Time exchanges spent in a route");

    public static readonly Histogram<double> SendDuration = CaravanDiagnostics.Meter.CreateHistogram<double>(
        CaravanSemanticConventions.SendDuration, "s", "Time sends to an endpoint took");

    public static readonly ObservableGauge<int> RoutesRunning = CaravanDiagnostics.Meter.CreateObservableGauge(
        CaravanSemanticConventions.RoutesRunning, ObserveRoutes, "{route}", "Started routes");

    /// <summary>Whether any per-exchange instrument of a route is listened to.</summary>
    public static bool RouteInstrumentsEnabled =>
        ExchangesTotal.Enabled || ExchangesSucceeded.Enabled || ExchangesFailed.Enabled || Inflight.Enabled || ExchangeDuration.Enabled;

    /// <summary>Makes the context visible to <see cref="RoutesRunning"/>.</summary>
    public static void Register(ICaravanContext context) => Contexts.TryAdd(context, 0);

    /// <summary>Removes the context from <see cref="RoutesRunning"/>.</summary>
    public static void Unregister(ICaravanContext context) => Contexts.TryRemove(context, out _);

    public static TagList RouteTags(ICaravanContext context, string routeId) =>
        new()
        {
            { CaravanSemanticConventions.ContextName, context.Name },
            { CaravanSemanticConventions.RouteId, routeId },
        };

    public static void RecordRedelivery(ICaravanContext context, string? routeId)
    {
        if (context.Telemetry.MetricsEnabled && Redeliveries.Enabled)
        {
            Redeliveries.Add(1, RouteTags(context, routeId ?? string.Empty));
        }
    }

    public static void RecordFailureHandled(ICaravanContext context, string? routeId)
    {
        if (context.Telemetry.MetricsEnabled && FailuresHandled.Enabled)
        {
            FailuresHandled.Add(1, RouteTags(context, routeId ?? string.Empty));
        }
    }

    private static IEnumerable<Measurement<int>> ObserveRoutes()
    {
        foreach (var context in Contexts.Keys)
        {
            if (!context.Telemetry.MetricsEnabled)
            {
                continue;
            }

            var running = context.Routes.Count(r => r.Status == ServiceStatus.Started);
            yield return new Measurement<int>(running, new KeyValuePair<string, object?>(CaravanSemanticConventions.ContextName, context.Name));
        }
    }
}
