namespace Caravan.Components.Direct;

/// <summary>Thrown when sending to a <c>direct</c> endpoint without an active consumer.</summary>
public sealed class DirectConsumerNotAvailableException(string message, Exchange exchange)
    : CaravanExchangeException(message, exchange);
