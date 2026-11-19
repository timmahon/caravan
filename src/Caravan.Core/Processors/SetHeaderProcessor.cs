using Caravan.Spi;

namespace Caravan.Processors;

/// <summary>Sets a header from an expression.</summary>
public sealed class SetHeaderProcessor(string name, IExpression expression) : IProcessor
{
    /// <summary>The header name.</summary>
    public string Name { get; } = name;

    /// <summary>Computes the header value.</summary>
    public IExpression Expression { get; } = expression;

    /// <inheritdoc />
    public ValueTask ProcessAsync(Exchange exchange)
    {
        exchange.Message.SetHeader(Name, Expression.Evaluate(exchange));
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public override string ToString() => $"SetHeader[{Name}, {Expression}]";
}
