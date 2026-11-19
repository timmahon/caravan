namespace Caravan;

/// <summary>
/// Thrown by the <c>aggregate</c> EIP when an exchange arrives for a correlation key whose group already completed and was closed
/// (<c>closeCorrelationKeyOnCompletion</c>); Camel's <c>ClosedCorrelationKeyException</c>.
/// </summary>
public class ClosedCorrelationKeyException : CaravanExchangeException
{
    /// <summary>Creates the exception.</summary>
    /// <param name="correlationKey">The closed correlation key.</param>
    /// <param name="exchange">The exchange that arrived for it.</param>
    public ClosedCorrelationKeyException(string correlationKey, Exchange? exchange)
        : base($"The correlation key [{correlationKey}] has been closed", exchange)
    {
        CorrelationKey = correlationKey;
    }

    /// <summary>The closed correlation key.</summary>
    public string CorrelationKey { get; }
}
