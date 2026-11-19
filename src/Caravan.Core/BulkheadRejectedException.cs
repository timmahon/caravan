namespace Caravan;

/// <summary>
/// Thrown by the <c>circuitBreaker</c> EIP when its bulkhead is full: the maximum number of concurrent calls are running and the waiting
/// queue is full (Resilience4j's <c>BulkheadFullException</c>).
/// </summary>
public class BulkheadRejectedException : CaravanExchangeException
{
    /// <summary>Creates the exception.</summary>
    /// <param name="message">Why the call was rejected.</param>
    /// <param name="exchange">The rejected exchange.</param>
    public BulkheadRejectedException(string message, Exchange? exchange)
        : base(message, exchange)
    {
    }
}
