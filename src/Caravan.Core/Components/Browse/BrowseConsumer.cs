using Caravan.Spi;
using Caravan.Support;

namespace Caravan.Components.Browse;

/// <summary>Receives the exchanges sent to its <c>browse</c> endpoint.</summary>
internal sealed class BrowseConsumer(BrowseEndpoint endpoint, IProcessor processor) : ConsumerBase(endpoint, processor)
{
    protected override Task DoStartAsync(CancellationToken cancellationToken)
    {
        endpoint.SetConsumer(Processor);
        return Task.CompletedTask;
    }

    protected override Task DoStopAsync(CancellationToken cancellationToken)
    {
        endpoint.SetConsumer(null);
        return Task.CompletedTask;
    }
}
