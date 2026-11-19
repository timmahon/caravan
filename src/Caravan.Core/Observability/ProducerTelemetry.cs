using System.Diagnostics;

namespace Caravan.Observability;

/// <summary>Spans, trace context propagation and metrics of sends to endpoints (used by every producer).</summary>
internal static class ProducerTelemetry
{
    /// <summary>Whether a send must be instrumented (someone listens, or an ambient trace context must be propagated).</summary>
    public static bool IsActive(ICaravanContext context)
    {
        var telemetry = context.Telemetry;
        return (telemetry.TracingEnabled && CaravanDiagnostics.ActivitySource.HasListeners())
            || (telemetry.MetricsEnabled && CaravanMetrics.SendDuration.Enabled)
            || (telemetry.PropagateTraceContext && Activity.Current is not null);
    }

    /// <summary>Sends through <paramref name="send"/> within a producer span.</summary>
    public static async ValueTask SendAsync(Func<Exchange, ValueTask> send, Exchange exchange, EndpointTelemetryInfo info)
    {
        var context = exchange.Context;
        var telemetry = context.Telemetry;
        Activity? activity = null;
        if (telemetry.TracingEnabled && CaravanDiagnostics.ActivitySource.HasListeners())
        {
            var tags = new ActivityTagsCollection
            {
                { CaravanSemanticConventions.ContextName, context.Name },
                { CaravanSemanticConventions.ExchangeId, exchange.ExchangeId },
                { CaravanSemanticConventions.EndpointUri, info.SanitizedUri },
                { CaravanSemanticConventions.ExchangePattern, exchange.Pattern.ToString() },
                { CaravanSemanticConventions.MessagingSystem, info.Scheme },
                { CaravanSemanticConventions.MessagingDestinationName, info.Path },
                { CaravanSemanticConventions.MessagingOperationType, "send" },
                { CaravanSemanticConventions.MessagingMessageId, exchange.Message.MessageId },
            };
            if (exchange.CurrentRouteId is { } routeId)
            {
                tags.Add(CaravanSemanticConventions.RouteId, routeId);
            }

            var kind = exchange.Pattern == ExchangePattern.InOut ? ActivityKind.Client : ActivityKind.Producer;
            activity = CaravanDiagnostics.ActivitySource.StartActivity("send " + info.Scheme + ":" + info.Path, kind, default(ActivityContext), tags);
        }

        if (telemetry.PropagateTraceContext && (activity ?? Activity.Current) is { } current)
        {
            TraceContextPropagator.Inject(exchange, current, telemetry.PropagateBaggage);
        }

        var measure = telemetry.MetricsEnabled && CaravanMetrics.SendDuration.Enabled;
        var start = measure ? context.TimeProvider.GetTimestamp() : 0;
        try
        {
            await send(exchange).ConfigureAwait(false);
            if (activity is not null && exchange.Exception is { } failure)
            {
                RouteTelemetry.SetError(activity, failure);
            }
        }
        catch (Exception e) when (activity is not null)
        {
            RouteTelemetry.SetError(activity, e);
            throw;
        }
        finally
        {
            activity?.Dispose();
            if (measure)
            {
                CaravanMetrics.SendDuration.Record(
                    context.TimeProvider.GetElapsedTime(start).TotalSeconds,
                    new TagList
                    {
                        { CaravanSemanticConventions.ContextName, context.Name },
                        { CaravanSemanticConventions.EndpointUri, info.Name },
                    });
            }
        }
    }
}
