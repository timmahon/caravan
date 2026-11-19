using System.Diagnostics;
using Caravan.Impl;

namespace Caravan.Observability;

/// <summary>Spans and metrics of exchanges in routes (used by the route's entry processor).</summary>
internal static class RouteTelemetry
{
    /// <summary>Starts the span of an exchange in a route, or returns null when tracing is off or nobody listens.</summary>
    public static Activity? StartActivity(Route route, Exchange exchange, bool entersContext)
    {
        var context = exchange.Context;
        var telemetry = context.Telemetry;
        if (!telemetry.TracingEnabled || !CaravanDiagnostics.ActivitySource.HasListeners())
        {
            return null;
        }

        var kind = entersContext ? (exchange.Pattern == ExchangePattern.InOut ? ActivityKind.Server : ActivityKind.Consumer) : ActivityKind.Internal;
        var extracted = false;
        ActivityContext parent = default;
        if (entersContext && telemetry.PropagateTraceContext)
        {
            extracted = TraceContextPropagator.TryExtract(exchange, out parent);
        }

        var info = route.TelemetryInfo;
        var tags = new ActivityTagsCollection
        {
            { CaravanSemanticConventions.ContextName, context.Name },
            { CaravanSemanticConventions.RouteId, route.Id },
            { CaravanSemanticConventions.ExchangeId, exchange.ExchangeId },
            { CaravanSemanticConventions.EndpointUri, info.SanitizedUri },
            { CaravanSemanticConventions.ExchangePattern, exchange.Pattern.ToString() },
            { CaravanSemanticConventions.MessagingSystem, info.Scheme },
            { CaravanSemanticConventions.MessagingDestinationName, info.Path },
            { CaravanSemanticConventions.MessagingOperationType, "process" },
            { CaravanSemanticConventions.MessagingMessageId, exchange.Message.MessageId },
        };
        var activity = CaravanDiagnostics.ActivitySource.StartActivity("route " + route.Id, kind, parent, tags);
        if (activity is not null && extracted && telemetry.PropagateBaggage)
        {
            TraceContextPropagator.ExtractBaggage(exchange, activity);
        }

        return activity;
    }

    /// <summary>Ends a span, recording the exchange's failure.</summary>
    public static void StopActivity(Activity activity, Exchange exchange)
    {
        if (exchange.Exception is { } exception)
        {
            SetError(activity, exception);
        }
        else if (exchange.ErrorHandlerHandled)
        {
            activity.SetTag("caravan.exchange.failure_handled", true);
        }

        activity.Dispose();
    }

    /// <summary>Marks a span as failed with an exception event.</summary>
    public static void SetError(Activity activity, Exception exception)
    {
        activity.SetStatus(ActivityStatusCode.Error, exception.Message);
        activity.AddException(exception);
    }

    /// <summary>Records that an exchange entered a route; returns whether metrics are recorded for it (then call <see cref="RecordEnd"/>).</summary>
    public static bool RecordStart(ICaravanContext context, string routeId)
    {
        if (!context.Telemetry.MetricsEnabled || !CaravanMetrics.RouteInstrumentsEnabled)
        {
            return false;
        }

        var tags = CaravanMetrics.RouteTags(context, routeId);
        CaravanMetrics.ExchangesTotal.Add(1, tags);
        CaravanMetrics.Inflight.Add(1, tags);
        return true;
    }

    /// <summary>Records that an exchange left a route.</summary>
    public static void RecordEnd(ICaravanContext context, string routeId, bool failed, TimeSpan elapsed)
    {
        var tags = CaravanMetrics.RouteTags(context, routeId);
        CaravanMetrics.Inflight.Add(-1, tags);
        (failed ? CaravanMetrics.ExchangesFailed : CaravanMetrics.ExchangesSucceeded).Add(1, tags);
        CaravanMetrics.ExchangeDuration.Record(elapsed.TotalSeconds, tags);
    }
}
