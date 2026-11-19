namespace Caravan.Language.Simple;

/// <summary><c>&amp;&amp;</c> or <c>||</c>, short-circuited.</summary>
internal sealed class LogicalNode(SimpleNode left, bool and, SimpleNode right) : SimpleNode
{
    /// <inheritdoc />
    public override object? Evaluate(Exchange exchange) => Matches(exchange);

    /// <inheritdoc />
    public override bool Matches(Exchange exchange) =>
        and ? left.Matches(exchange) && right.Matches(exchange) : left.Matches(exchange) || right.Matches(exchange);
}
