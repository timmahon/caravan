namespace Caravan;

/// <summary>Thrown by the <c>validate</c> EIP when the predicate does not match (Camel's <c>PredicateValidationException</c>).</summary>
public class PredicateValidationException : CaravanExchangeException
{
    /// <summary>Creates the exception.</summary>
    /// <param name="exchange">The exchange that failed validation.</param>
    /// <param name="predicate">The predicate that did not match.</param>
    public PredicateValidationException(Exchange exchange, IPredicate predicate)
        : base($"Validation failed for Predicate[{predicate}]", exchange)
    {
        Predicate = predicate;
    }

    /// <summary>The predicate that did not match.</summary>
    public IPredicate Predicate { get; }
}
