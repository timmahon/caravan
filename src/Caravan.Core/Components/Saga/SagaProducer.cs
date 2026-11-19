using Caravan.Support;

namespace Caravan.Components.Saga;

/// <summary>Completes or compensates the saga named by the exchange's <see cref="CaravanHeaders.SagaLongRunningAction"/> header.</summary>
internal sealed class SagaProducer(SagaEndpoint endpoint) : ProducerBase(endpoint)
{
    public override async ValueTask ProcessAsync(Exchange exchange)
    {
        var id = exchange.Message.GetHeader(CaravanHeaders.SagaLongRunningAction)?.ToString();
        if (string.IsNullOrEmpty(id))
        {
            throw new CaravanExchangeException("Current exchange is not bound to a saga", exchange);
        }

        var saga = await endpoint.Context.GetSagaService().GetSagaAsync(id).ConfigureAwait(false)
            ?? throw new CaravanExchangeException($"Saga {id} does not exist or has already finished", exchange);
        if (endpoint.Action == SagaEndpointAction.Complete)
        {
            await saga.CompleteAsync(exchange).ConfigureAwait(false);
        }
        else
        {
            await saga.CompensateAsync(exchange).ConfigureAwait(false);
        }
    }
}
