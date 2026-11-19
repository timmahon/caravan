using Caravan.Spi;

namespace Caravan.Processors;

/// <summary>The <c>script</c> EIP: evaluates <see cref="Expression"/> for its side effects; the message is not changed by the result.</summary>
public sealed class ScriptProcessor(IExpression expression) : IProcessor
{
    /// <summary>The expression to evaluate.</summary>
    public IExpression Expression { get; } = expression;

    /// <inheritdoc />
    public ValueTask ProcessAsync(Exchange exchange)
    {
        Expression.Evaluate(exchange);
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public override string ToString() => $"Script[{Expression}]";
}
