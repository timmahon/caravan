using Caravan.Spi;
using Caravan.Support;
using Microsoft.Extensions.Logging;

namespace Caravan.Processors;

/// <summary>The <c>log</c> EIP: logs a message computed from the exchange. The message is only evaluated when the level is enabled.</summary>
public sealed class LogProcessor(IExpression message, LogLevel level, ILogger logger) : IProcessor
{
    /// <summary>Computes the message.</summary>
    public IExpression Message { get; } = message;

    /// <summary>The level to log at.</summary>
    public LogLevel Level { get; } = level;

    /// <summary>The logger written to.</summary>
    public ILogger Logger { get; } = logger;

    /// <inheritdoc />
    public ValueTask ProcessAsync(Exchange exchange)
    {
        if (Logger.IsEnabled(Level))
        {
            Logger.Log(Level, "{Message}", ObjectHelper.ToText(exchange, Message.Evaluate(exchange)));
        }

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public override string ToString() => $"Log[{Message}]";
}
