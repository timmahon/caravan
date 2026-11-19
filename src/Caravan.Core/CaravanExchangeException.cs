namespace Caravan;

/// <summary>An exception tied to the exchange being processed.</summary>
public class CaravanExchangeException : CaravanException
{
    /// <summary>Creates an exception; the exchange id is appended to <paramref name="message"/> when an exchange is given.</summary>
    public CaravanExchangeException(string message, Exchange? exchange, Exception? innerException = null)
        : base(exchange is null ? message : $"{message}. Exchange[{exchange.ExchangeId}]", innerException)
    {
        Exchange = exchange;
    }

    /// <summary>The exchange being processed when the error occurred, if known.</summary>
    public Exchange? Exchange { get; }
}
