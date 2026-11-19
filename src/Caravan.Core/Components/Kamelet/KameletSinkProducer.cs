using Caravan.Support;

namespace Caravan.Components.Kamelet;

/// <summary>The producer of <c>kamelet:sink</c>: leaves the exchange unchanged, or hands it to the route consuming from the Kamelet.</summary>
internal sealed class KameletSinkProducer(KameletSinkEndpoint endpoint) : ProducerBase(endpoint)
{
    public override ValueTask ProcessAsync(Exchange exchange)
    {
        if (endpoint.RouteId is not { } routeId)
        {
            return ValueTask.CompletedTask;
        }

        var consumer = endpoint.KameletComponent.GetSink(routeId)
            ?? throw new CaravanExchangeException($"No consumer on kamelet route {routeId} (the route consuming from the Kamelet is not started)", exchange);
        return consumer.ProcessAsync(exchange);
    }
}
