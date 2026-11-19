namespace Caravan.Language.Simple;

/// <summary><c>value ?: fallback</c>: the fallback when the value is null, an empty string or <c>false</c>.</summary>
internal sealed class ElvisNode(SimpleNode value, SimpleNode fallback) : SimpleNode
{
    /// <inheritdoc />
    public override object? Evaluate(Exchange exchange) =>
        value.Evaluate(exchange) is { } result and not "" and not false ? result : fallback.Evaluate(exchange);
}
