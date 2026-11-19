using Caravan.Spi;

namespace Caravan.Processors;

/// <summary>Sets a variable from an expression (<c>setVariable</c>).</summary>
public sealed class SetVariableProcessor(string name, IExpression expression) : IProcessor
{
    /// <summary>The variable name, optionally prefixed by a repository id.</summary>
    public string Name { get; } = name;

    /// <summary>Computes the value.</summary>
    public IExpression Expression { get; } = expression;

    /// <inheritdoc />
    public ValueTask ProcessAsync(Exchange exchange)
    {
        exchange.SetVariable(Name, Expression.Evaluate(exchange));
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public override string ToString() => $"SetVariable[{Name}, {Expression}]";
}
