using Caravan.Spi;

namespace Caravan.Processors;

/// <summary>Sets an exchange property from an expression (<c>setProperty</c>).</summary>
public sealed class SetPropertyProcessor(string name, IExpression expression) : IProcessor
{
    /// <summary>The property name.</summary>
    public string Name { get; } = name;

    /// <summary>Computes the property value.</summary>
    public IExpression Expression { get; } = expression;

    /// <inheritdoc />
    public ValueTask ProcessAsync(Exchange exchange)
    {
        exchange.SetProperty(Name, Expression.Evaluate(exchange));
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public override string ToString() => $"SetProperty[{Name}, {Expression}]";
}
