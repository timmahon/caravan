using Caravan.Management;
using Caravan.Model;
using Caravan.Spi;

namespace Caravan.Observability;

/// <summary>
/// Wraps every node of a route in an <see cref="InstrumentedNodeProcessor"/> recording node statistics, message history, tracer and backlog
/// tracer steps and node spans, as configured on the context when the route is created (Camel's instrumentation and tracing intercept
/// strategies, combined so that a node gets one wrapper).
/// </summary>
internal sealed class NodeInstrumentationStrategy : IInterceptStrategy
{
    private readonly bool _statistics;
    private readonly bool _history;
    private readonly CaravanTracer? _tracer;
    private readonly BacklogTracer? _backlogTracer;
    private readonly bool _spans;

    private NodeInstrumentationStrategy(bool statistics, bool history, CaravanTracer? tracer, BacklogTracer? backlogTracer, bool spans)
    {
        _statistics = statistics;
        _history = history;
        _tracer = tracer;
        _backlogTracer = backlogTracer;
        _spans = spans;
    }

    /// <summary>The strategy for the routes created now, or null when nothing needs it.</summary>
    public static NodeInstrumentationStrategy? Create(ICaravanContext context)
    {
        var statistics = context.Management is DefaultManagementService { NodeStatisticsEnabled: true };
        var history = context.MessageHistory;
        var tracer = context.Tracer.Installed ? context.Tracer : null;
        var backlog = context.BacklogTracer.Installed ? context.BacklogTracer : null;
        var spans = context.Telemetry.TracingEnabled && context.Telemetry.TraceProcessors;
        return statistics || history || tracer is not null || backlog is not null || spans
            ? new NodeInstrumentationStrategy(statistics, history, tracer, backlog, spans)
            : null;
    }

    /// <inheritdoc />
    public IProcessor WrapProcessor(RouteContext routeContext, ProcessorDefinition definition, IProcessor target) =>
        new InstrumentedNodeProcessor(
            routeContext.RouteId,
            definition,
            target,
            _statistics ? routeContext.GetProcessorCounters(definition).Counters : null,
            _history,
            _tracer,
            _backlogTracer,
            _spans);
}
