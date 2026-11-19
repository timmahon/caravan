using Caravan.Management;

namespace Caravan.Hosting;

/// <summary>
/// Options of the context created by <c>AddCaravan</c>, bound from the <c>Caravan</c> configuration section (Camel main's <c>camel.main.*</c>);
/// the lambda given to <c>AddCaravan</c> runs after binding. Null values keep the context's defaults.
/// </summary>
public sealed class CaravanOptions
{
    /// <summary>The configuration section the options are bound from (<c>Caravan</c>).</summary>
    public const string SectionName = "Caravan";

    /// <summary>The context name (<see cref="ICaravanContext.Name"/>).</summary>
    public string? Name { get; set; }

    /// <summary>Whether routes start with the context (<see cref="ICaravanContext.AutoStartup"/>, default true).</summary>
    public bool AutoStartup { get; set; } = true;

    /// <summary>Whether stream caching is enabled (<see cref="Streams.StreamCachingStrategy.Enabled"/>); null keeps the default (true).</summary>
    public bool? StreamCaching { get; set; }

    /// <summary>The graceful shutdown timeout (<see cref="Spi.IShutdownStrategy.Timeout"/>); null keeps the default (45 s).</summary>
    public TimeSpan? ShutdownTimeout { get; set; }

    /// <summary>Patterns of the routes to include (<see cref="ICaravanContext.RouteFilterIncludePattern"/>).</summary>
    public string? RouteFilterIncludePattern { get; set; }

    /// <summary>Patterns of the routes to exclude (<see cref="ICaravanContext.RouteFilterExcludePattern"/>).</summary>
    public string? RouteFilterExcludePattern { get; set; }

    /// <summary>Whether the tracer logs every step of every exchange (<see cref="ICaravanContext.Tracing"/>, default false).</summary>
    public bool TracingEnabled { get; set; }

    /// <summary>Whether routes are prepared for tracing so that it can be enabled at runtime (<see cref="Observability.CaravanTracer.Standby"/>).</summary>
    public bool TracingStandby { get; set; }

    /// <summary>Patterns of the routes or nodes to trace (<see cref="Observability.CaravanTracer.TracePattern"/>).</summary>
    public string? TracePattern { get; set; }

    /// <summary>Whether the backlog tracer records steps (<see cref="Observability.BacklogTracer.Enabled"/>, default false).</summary>
    public bool BacklogTracing { get; set; }

    /// <summary>Whether exchanges record their message history (<see cref="ICaravanContext.MessageHistory"/>, default false).</summary>
    public bool MessageHistory { get; set; }

    /// <summary>Whether a copy of the original message is kept (<see cref="ICaravanContext.AllowUseOriginalMessage"/>, default false).</summary>
    public bool AllowUseOriginalMessage { get; set; }

    /// <summary>Whether OpenTelemetry spans are created (<see cref="Observability.TelemetryOptions.TracingEnabled"/>, default true).</summary>
    public bool OpenTelemetryTracing { get; set; } = true;

    /// <summary>Whether OpenTelemetry metrics are recorded (<see cref="Observability.TelemetryOptions.MetricsEnabled"/>, default true).</summary>
    public bool OpenTelemetryMetrics { get; set; } = true;

    /// <summary>Whether a span is created per node (<see cref="Observability.TelemetryOptions.TraceProcessors"/>, default false).</summary>
    public bool TraceProcessors { get; set; }

    /// <summary>Whether the W3C trace context is propagated in message headers (<see cref="Observability.TelemetryOptions.PropagateTraceContext"/>).</summary>
    public bool PropagateTraceContext { get; set; } = true;

    /// <summary>Which management counters are kept (<see cref="IManagementService.StatisticsLevel"/>); null keeps the default.</summary>
    public ManagementStatisticsLevel? StatisticsLevel { get; set; }

    /// <summary>Whether the context uses the <see cref="TimeProvider"/> registered in the service provider, if any (default true).</summary>
    public bool UseServiceTimeProvider { get; set; } = true;
}
