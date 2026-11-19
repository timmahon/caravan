using Caravan.Spi;
using Caravan.Support;

namespace Caravan.Components.Direct;

internal sealed class DirectConsumer(DirectEndpoint endpoint, IProcessor processor) : ConsumerBase(endpoint, processor)
{
    protected override Task DoStartAsync(CancellationToken cancellationToken)
    {
        endpoint.DirectComponent.AddConsumer(endpoint.Name, this);
        return Task.CompletedTask;
    }

    protected override Task DoStopAsync(CancellationToken cancellationToken)
    {
        endpoint.DirectComponent.RemoveConsumer(endpoint.Name, this);
        return Task.CompletedTask;
    }
}
