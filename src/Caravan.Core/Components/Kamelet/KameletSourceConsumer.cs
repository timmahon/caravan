using Caravan.Spi;
using Caravan.Support;

namespace Caravan.Components.Kamelet;

/// <summary>Registers the route of a <see cref="KameletSourceEndpoint"/> with its component while started.</summary>
internal sealed class KameletSourceConsumer(KameletSourceEndpoint endpoint, IProcessor processor) : ConsumerBase(endpoint, processor)
{
    protected override Task DoStartAsync(CancellationToken cancellationToken)
    {
        endpoint.KameletComponent.AddSource(endpoint.RouteId, Processor);
        return Task.CompletedTask;
    }

    protected override Task DoStopAsync(CancellationToken cancellationToken)
    {
        endpoint.KameletComponent.RemoveSource(endpoint.RouteId, Processor);
        return Task.CompletedTask;
    }
}
