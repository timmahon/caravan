namespace Caravan;

/// <summary>Thrown by the <c>throttle</c> EIP with <c>rejectExecution</c> when an exchange exceeds the limit (Camel's <c>ThrottlerRejectedExecutionException</c>).</summary>
public class ThrottlerRejectedExecutionException : CaravanExchangeException
{
    /// <summary>Creates the exception.</summary>
    /// <param name="message">Why the exchange was rejected.</param>
    /// <param name="exchange">The rejected exchange.</param>
    public ThrottlerRejectedExecutionException(string message, Exchange? exchange)
        : base(message, exchange)
    {
    }
}
