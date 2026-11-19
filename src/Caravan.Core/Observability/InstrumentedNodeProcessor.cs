using System.Diagnostics;
using Caravan.Management;
using Caravan.Model;
using Caravan.Spi;

namespace Caravan.Observability;

/// <summary>Records statistics, message history, tracing and a span around one node (see <see cref="NodeInstrumentationStrategy"/>).</summary>
internal sealed class InstrumentedNodeProcessor(
    string routeId,
    ProcessorDefinition definition,
    IProcessor target,
    ProcessingCounters? counters,
    bool history,
    CaravanTracer? tracer,
    BacklogTracer? backlogTracer,
    bool spans) : INavigableProcessor
{
    public IReadOnlyList<IProcessor> Next => [target];

    public async ValueTask ProcessAsync(Exchange exchange)
    {
        var context = exchange.Context;
        var clock = context.TimeProvider;
        var start = clock.GetTimestamp();
        var now = counters is not null || history ? clock.GetUtcNow() : default;
        counters?.Begin(now);
        var entry = history ? MessageHistoryRecorder.Add(exchange, routeId, definition.Id ?? definition.ShortName, definition.Label, now) : null;
        tracer?.TraceNode(exchange, routeId, definition);
        var traced = backlogTracer?.Trace(exchange, routeId, definition);
        var activity = spans && context.Telemetry.TracingEnabled && CaravanDiagnostics.ActivitySource.HasListeners() ? StartActivity(exchange) : null;
        try
        {
            await target.ProcessAsync(exchange).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            exchange.Exception = e;
        }
        finally
        {
            var elapsed = clock.GetElapsedTime(start);
            counters?.End(elapsed, exchange.IsFailed, clock.GetUtcNow());
            entry?.Done(elapsed);
            traced?.Done(elapsed, exchange.Exception);
            if (activity is not null)
            {
                if (exchange.Exception is { } exception)
                {
                    RouteTelemetry.SetError(activity, exception);
                }

                activity.Dispose();
            }
        }
    }

    public override string ToString() => target.ToString() ?? base.ToString()!;

    private Activity? StartActivity(Exchange exchange)
    {
        var tags = new ActivityTagsCollection
        {
            { CaravanSemanticConventions.ContextName, exchange.Context.Name },
            { CaravanSemanticConventions.RouteId, routeId },
            { CaravanSemanticConventions.ExchangeId, exchange.ExchangeId },
            { CaravanSemanticConventions.NodeId, definition.Id },
            { CaravanSemanticConventions.NodeLabel, definition.Label },
        };
        return CaravanDiagnostics.ActivitySource.StartActivity(definition.ShortName + " " + definition.Id, ActivityKind.Internal, default(ActivityContext), tags);
    }
}
