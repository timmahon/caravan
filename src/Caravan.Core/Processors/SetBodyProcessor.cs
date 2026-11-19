using Caravan.Spi;

namespace Caravan.Processors;

/// <summary>Sets the message body from an expression (<c>setBody</c> / <c>transform</c>).</summary>
public sealed class SetBodyProcessor(IExpression expression) : IProcessor
{
    /// <summary>Computes the new body.</summary>
    public IExpression Expression { get; } = expression;

    /// <inheritdoc />
    public ValueTask ProcessAsync(Exchange exchange)
    {
        exchange.Message.Body = Expression.Evaluate(exchange);
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public override string ToString() => $"SetBody[{Expression}]";
}
