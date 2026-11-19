using Caravan.Saga;
using Caravan.Spi;
using Microsoft.Extensions.Logging;

namespace Caravan.Processors;

/// <summary>Ends a saga started by a <c>saga</c> EIP covering the rest of the exchange when the exchange's unit of work completes.</summary>
internal sealed class SagaCompletionSynchronization(ISagaCoordinator saga) : ISynchronization
{
    public async ValueTask OnCompleteAsync(Exchange exchange)
    {
        try
        {
            await saga.CompleteAsync(exchange).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            Log(exchange, e, "complete");
        }
    }

    public async ValueTask OnFailureAsync(Exchange exchange)
    {
        try
        {
            await saga.CompensateAsync(exchange).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            Log(exchange, e, "compensate");
        }
    }

    private void Log(Exchange exchange, Exception e, string action) =>
        exchange.Context.LoggerFactory.CreateLogger<SagaCompletionSynchronization>().LogWarning(e, "Cannot {Action} saga {SagaId}", action, saga.Id);
}
