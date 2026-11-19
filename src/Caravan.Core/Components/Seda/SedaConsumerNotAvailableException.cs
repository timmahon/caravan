namespace Caravan.Components.Seda;

/// <summary>Thrown when sending to a <c>seda</c> endpoint with <c>failIfNoConsumers</c> and no active consumer.</summary>
public sealed class SedaConsumerNotAvailableException(string message, Exchange exchange)
    : CaravanExchangeException(message, exchange);
