using Caravan.Spi;

namespace Caravan.Components.Seda;

/// <summary>Completes a waiting producer when the queued copy is done; never handed over (it belongs to that copy).</summary>
internal sealed class SedaReplySynchronization(TaskCompletionSource<Exchange> reply) : ISynchronization
{
    public bool AllowHandover => false;

    public ValueTask OnCompleteAsync(Exchange exchange)
    {
        reply.TrySetResult(exchange);
        return ValueTask.CompletedTask;
    }

    public ValueTask OnFailureAsync(Exchange exchange)
    {
        reply.TrySetResult(exchange);
        return ValueTask.CompletedTask;
    }
}
