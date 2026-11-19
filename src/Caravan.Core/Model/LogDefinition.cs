using Caravan.Processors;
using Caravan.Spi;
using Caravan.Support;
using Microsoft.Extensions.Logging;

namespace Caravan.Model;

/// <summary>
/// The <c>log</c> EIP: logs <see cref="Message"/> (with <c>${...}</c> tokens, see <see cref="TokenTemplate"/>) through
/// the context's logger factory. The logger name defaults to the route id, as in Camel.
/// </summary>
public sealed class LogDefinition(string message) : ProcessorDefinition
{
    /// <summary>The message template.</summary>
    public string Message { get; set; } = message;

    /// <summary>The level to log at (default <c>Information</c>).</summary>
    public LogLevel LoggingLevel { get; set; } = LogLevel.Information;

    /// <summary>The logger name (category); null uses the route id.</summary>
    public string? LogName { get; set; }

    /// <inheritdoc />
    public override string ShortName => "log";

    /// <inheritdoc />
    public override string Label => $"log[{Message}]";

    /// <inheritdoc />
    public override IProcessor CreateProcessor(RouteContext routeContext) =>
        new LogProcessor(
            TokenTemplate.Parse(Message),
            LoggingLevel,
            routeContext.Context.LoggerFactory.CreateLogger(LogName ?? routeContext.RouteId));
}
