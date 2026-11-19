namespace Caravan;

/// <summary>Thrown by the <c>rollback</c> EIP to fail and roll back an exchange (Camel's <c>RollbackExchangeException</c>).</summary>
public class RollbackExchangeException : CaravanExchangeException
{
    /// <summary>Creates the exception for <paramref name="exchange"/>.</summary>
    public RollbackExchangeException(string? message, Exchange? exchange, Exception? innerException = null)
        : base(message ?? "Intended rollback", exchange, innerException)
    {
    }
}
