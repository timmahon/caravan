namespace Caravan.Language.Simple;

/// <summary><c>condition ? whenTrue : whenFalse</c>.</summary>
internal sealed class TernaryNode(SimpleNode condition, SimpleNode whenTrue, SimpleNode whenFalse) : SimpleNode
{
    /// <inheritdoc />
    public override object? Evaluate(Exchange exchange) =>
        condition.Matches(exchange) ? whenTrue.Evaluate(exchange) : whenFalse.Evaluate(exchange);
}
