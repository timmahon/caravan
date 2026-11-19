namespace Caravan;

/// <summary>Thrown by the <c>threads</c> EIP when its pool and queue are full and the rejected policy is <c>Abort</c> (Java's <c>RejectedExecutionException</c>).</summary>
public class RejectedExecutionException : CaravanExchangeException
{
    /// <summary>Creates the exception.</summary>
    /// <param name="message">Why the exchange was rejected.</param>
    /// <param name="exchange">The rejected exchange.</param>
    public RejectedExecutionException(string message, Exchange? exchange)
        : base(message, exchange)
    {
    }
}
