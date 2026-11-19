namespace Caravan.Language.Simple;

/// <summary>A node of a compiled Simple expression. Nodes are immutable and evaluated concurrently.</summary>
internal abstract class SimpleNode
{
    /// <summary>Writes a new value to the source of this node (body, header, exchange property), for <c>++</c>/<c>--</c>; null when read-only.</summary>
    public virtual Action<Exchange, object?>? Setter => null;

    /// <summary>Evaluates the node.</summary>
    public abstract object? Evaluate(Exchange exchange);

    /// <summary>Evaluates the node as a condition.</summary>
    public virtual bool Matches(Exchange exchange) => PredicateHelper.ToBoolean(exchange, Evaluate(exchange));
}
