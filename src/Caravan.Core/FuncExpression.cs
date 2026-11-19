namespace Caravan;

/// <summary>An expression backed by a function.</summary>
public sealed class FuncExpression(Func<Exchange, object?> func, string? text = null) : IExpression
{
    /// <inheritdoc />
    public object? Evaluate(Exchange exchange) => func(exchange);

    /// <inheritdoc />
    public override string ToString() => text ?? "expression";
}
