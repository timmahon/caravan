namespace Caravan;

/// <summary>
/// Thrown by the stream <c>resequence</c> EIP with <c>rejectOld</c> when an exchange arrives whose sequence number is lower than that
/// of the last delivered exchange (Camel's <c>MessageRejectedException</c>).
/// </summary>
public class MessageRejectedException : CaravanExchangeException
{
    /// <summary>Creates the exception.</summary>
    /// <param name="message">The reason.</param>
    /// <param name="exchange">The rejected exchange.</param>
    public MessageRejectedException(string message, Exchange? exchange)
        : base(message, exchange)
    {
    }
}
