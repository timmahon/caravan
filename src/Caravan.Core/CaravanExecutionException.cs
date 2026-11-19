namespace Caravan;

/// <summary>Thrown by <see cref="IProducerTemplate"/> when routing an exchange failed.</summary>
public class CaravanExecutionException(string message, Exchange exchange, Exception? innerException)
    : CaravanExchangeException(message, exchange, innerException);
