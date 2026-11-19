namespace Caravan;

/// <summary>Evaluates a condition on an exchange (Camel's <c>Predicate</c>).</summary>
public interface IPredicate
{
    /// <summary>Whether the predicate holds for <paramref name="exchange"/>.</summary>
    bool Matches(Exchange exchange);
}
