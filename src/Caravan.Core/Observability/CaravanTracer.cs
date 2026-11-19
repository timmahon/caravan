using Caravan.Model;
using Caravan.Support;
using Microsoft.Extensions.Logging;

namespace Caravan.Observability;

/// <summary>
/// Camel's <c>Tracer</c>: logs every step of every exchange (route input and output, and each node) at <see cref="LogLevel"/> with the
/// exchange formatted by <see cref="Formatter"/>, in the <see cref="LoggerName"/> category. Turn it on with <see cref="ICaravanContext.Tracing"/>
/// before routes are created, or keep <see cref="Standby"/> on to switch <see cref="Enabled"/> at runtime.
/// </summary>
public sealed class CaravanTracer
{
    /// <summary>The default logger category.</summary>
    public const string DefaultLoggerName = "Caravan.Tracing";

    /// <summary>Whether steps are logged (<see cref="ICaravanContext.Tracing"/>).</summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Whether routes are prepared for tracing even when <see cref="Enabled"/> is false when they are created, so that tracing can be turned
    /// on later (Camel's <c>tracingStandby</c>).
    /// </summary>
    public bool Standby { get; set; }

    /// <summary>
    /// Comma-separated patterns (exact, wildcard or regular expression) of the route ids or node ids to trace (Camel's <c>tracePattern</c>);
    /// null traces everything.
    /// </summary>
    public string? TracePattern { get; set; }

    /// <summary>Whether the route input (<c>*--&gt;</c>) and output (<c>*&lt;--</c>) are logged too (default true).</summary>
    public bool TraceBeforeAndAfterRoute { get; set; } = true;

    /// <summary>The logger category (default <c>Caravan.Tracing</c>).</summary>
    public string LoggerName { get; set; } = DefaultLoggerName;

    /// <summary>The log level (default Information).</summary>
    public LogLevel LogLevel { get; set; } = LogLevel.Information;

    /// <summary>Formats the exchange (default: id, pattern, headers, body type and body, single line).</summary>
    public ExchangeFormatter Formatter { get; set; } = new()
    {
        ShowExchangeId = true,
        ShowHeaders = true,
        ShowBodyType = true,
        ShowBody = true,
        MaxChars = 1000,
    };

    /// <summary>Whether routes created now need the tracing wrapper.</summary>
    internal bool Installed => Enabled || Standby;

    /// <summary>Logs that an exchange reaches a node.</summary>
    internal void TraceNode(Exchange exchange, string routeId, ProcessorDefinition definition)
    {
        if (!Enabled || !Matches(routeId, definition.Id))
        {
            return;
        }

        var logger = exchange.Context.LoggerFactory.CreateLogger(LoggerName);
        if (logger.IsEnabled(LogLevel))
        {
            logger.Log(LogLevel, "[{RouteId}] [{NodeId}] {Label} {Exchange}", routeId, definition.Id, definition.Label, Formatter.Format(exchange));
        }
    }

    /// <summary>Logs that an exchange enters (<paramref name="input"/>) or leaves a route.</summary>
    internal void TraceRoute(Exchange exchange, string routeId, string endpointUri, bool input)
    {
        if (!Enabled || !TraceBeforeAndAfterRoute || !Matches(routeId, null))
        {
            return;
        }

        var logger = exchange.Context.LoggerFactory.CreateLogger(LoggerName);
        if (logger.IsEnabled(LogLevel))
        {
            logger.Log(
                LogLevel,
                "[{RouteId}] {Direction} {Endpoint} {Exchange}",
                routeId,
                input ? "*-->" : "*<--",
                UriSanitizer.Sanitize(endpointUri),
                Formatter.Format(exchange));
        }
    }

    private bool Matches(string routeId, string? nodeId)
    {
        if (TracePattern is not { } patterns)
        {
            return true;
        }

        foreach (var pattern in patterns.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (PatternHelper.Matches(routeId, pattern) || (nodeId is not null && PatternHelper.Matches(nodeId, pattern)))
            {
                return true;
            }
        }

        return false;
    }
}
