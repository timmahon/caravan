using Caravan.Support;

namespace Caravan.Components.Kamelet;

/// <summary>Calls the route of a <see cref="KameletEndpoint"/>, creating it on first use.</summary>
internal sealed class KameletProducer(KameletEndpoint endpoint) : ProducerBase(endpoint)
{
    public override async ValueTask ProcessAsync(Exchange exchange)
    {
        var routeId = await endpoint.EnsureRouteAsync(exchange.CancellationToken).ConfigureAwait(false);
        var source = endpoint.KameletComponent.GetSource(routeId)
            ?? throw new CaravanExchangeException($"The kamelet route {routeId} is not started (no consumer on kamelet:source)", exchange);
        await source.ProcessAsync(exchange).ConfigureAwait(false);
    }
}
