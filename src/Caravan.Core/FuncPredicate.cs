namespace Caravan;

/// <summary>A predicate backed by a function.</summary>
public sealed class FuncPredicate(Func<Exchange, bool> func, string? text = null) : IPredicate
{
    /// <inheritdoc />
    public bool Matches(Exchange exchange) => func(exchange);

    /// <inheritdoc />
    public override string ToString() => text ?? "predicate";
}
