using Caravan.Support;

namespace Caravan.Components.Browse;

/// <summary>Records the exchange on its <c>browse</c> endpoint.</summary>
internal sealed class BrowseProducer(BrowseEndpoint endpoint) : ProducerBase(endpoint)
{
    public override ValueTask ProcessAsync(Exchange exchange) => endpoint.OnExchangeAsync(exchange);
}
