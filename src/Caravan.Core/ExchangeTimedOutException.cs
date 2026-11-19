namespace Caravan;

/// <summary>The exchange did not complete within the allowed time (e.g. a <c>seda</c> request/reply).</summary>
public class ExchangeTimedOutException : CaravanExchangeException
{
    /// <summary>Creates the exception.</summary>
    /// <param name="exchange">The exchange that timed out.</param>
    /// <param name="timeout">The time that was allowed.</param>
    /// <param name="message">Optional detail; defaults to Camel's wording.</param>
    public ExchangeTimedOutException(Exchange? exchange, TimeSpan timeout, string? message = null)
        : base(message is null
            ? $"The OUT message was not received within: {(long)timeout.TotalMilliseconds} millis"
            : $"The OUT message was not received within: {(long)timeout.TotalMilliseconds} millis due {message}", exchange)
    {
        Timeout = timeout;
    }

    /// <summary>The time that was allowed.</summary>
    public TimeSpan Timeout { get; }
}
