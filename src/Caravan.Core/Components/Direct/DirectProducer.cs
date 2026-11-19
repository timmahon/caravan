using Caravan.Support;

namespace Caravan.Components.Direct;

internal sealed class DirectProducer(DirectEndpoint endpoint) : ProducerBase(endpoint)
{
    public override async ValueTask ProcessAsync(Exchange exchange)
    {
        var component = endpoint.DirectComponent;
        var consumer = component.GetConsumer(endpoint.Name);
        if (consumer is null && endpoint.Block)
        {
            consumer = await component.WaitForConsumerAsync(endpoint.Name, endpoint.Timeout, exchange.CancellationToken).ConfigureAwait(false);
        }

        if (consumer is null)
        {
            if (endpoint.FailIfNoConsumers)
            {
                throw new DirectConsumerNotAvailableException($"No consumers available on endpoint: {endpoint.EndpointUri}", exchange);
            }

            return;
        }

        await consumer.Processor.ProcessAsync(exchange).ConfigureAwait(false);
    }
}
