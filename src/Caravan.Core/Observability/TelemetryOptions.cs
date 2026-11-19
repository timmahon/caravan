namespace Caravan.Observability;

/// <summary>
/// OpenTelemetry-style tracing and metrics of a context (<see cref="ICaravanContext.Telemetry"/>). Spans and measurements are only produced
/// while a listener is attached to <see cref="CaravanDiagnostics.ActivitySource"/> / <see cref="CaravanDiagnostics.Meter"/>.
/// </summary>
public sealed class TelemetryOptions
{
    /// <summary>Whether spans are created (default true): one per exchange per route and one per send to an endpoint.</summary>
    public bool TracingEnabled { get; set; } = true;

    /// <summary>
    /// Whether a child span is created for every node of a route (default false: route and send spans only). Read when routes are created.
    /// </summary>
    public bool TraceProcessors { get; set; }

    /// <summary>
    /// Whether the W3C trace context (<c>traceparent</c>/<c>tracestate</c> headers) is injected into the message on every send and extracted
    /// when a route receives a new exchange (default true), so that traces continue across <c>seda</c>/<c>vm</c> hops and transports.
    /// </summary>
    public bool PropagateTraceContext { get; set; } = true;

    /// <summary>Whether the <c>baggage</c> header is propagated too (default true).</summary>
    public bool PropagateBaggage { get; set; } = true;

    /// <summary>Whether metrics are recorded (default true).</summary>
    public bool MetricsEnabled { get; set; } = true;
}
