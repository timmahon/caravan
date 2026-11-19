using Caravan.Support;
using Microsoft.Extensions.Logging;

namespace Caravan.Components.Seda;

/// <summary>
/// Puts a correlated copy of the exchange on the queue. Without waiting, the exchange's completion callbacks are handed
/// over to the copy. When waiting (<see cref="SedaEndpoint.WaitForTaskToComplete"/>), the copy's result is copied back
/// onto the caller's exchange when the consumer completes it, or the exchange fails with
/// <see cref="ExchangeTimedOutException"/> after <see cref="SedaEndpoint.Timeout"/> (the copy is then cancelled and
/// skipped if still queued).
/// </summary>
internal sealed class SedaProducer(SedaEndpoint endpoint) : ProducerBase(endpoint)
{
    private readonly ILogger _logger = endpoint.Context.LoggerFactory.CreateLogger<SedaProducer>();

    public override async ValueTask ProcessAsync(Exchange exchange)
    {
        var queue = endpoint.Queue;
        var wait = exchange.GetProperty(ExchangePropertyKeys.AsyncWait, endpoint.WaitForTaskToComplete);
        if (!queue.HasConsumers)
        {
            if (endpoint.FailIfNoConsumers)
            {
                throw new SedaConsumerNotAvailableException($"No consumers available on endpoint: {endpoint.EndpointUri}", exchange);
            }

            if (endpoint.DiscardIfNoConsumers)
            {
                _logger.LogDebug("Discarding exchange {ExchangeId} as there are no consumers on {Uri}", exchange.ExchangeId, endpoint.EndpointUri);
                return;
            }
        }

        if (wait == WaitForTaskToComplete.Always || (wait == WaitForTaskToComplete.IfReplyExpected && exchange.Pattern == ExchangePattern.InOut))
        {
            await SendAndWaitAsync(exchange).ConfigureAwait(false);
        }
        else
        {
            var copy = PrepareCopy(exchange);
            exchange.UnitOfWork?.HandoverSynchronization(copy);
            foreach (var pending in exchange.TakePendingCompletions() ?? [])
            {
                if (pending.AllowHandover)
                {
                    copy.AddOnCompletion(pending);
                }
                else
                {
                    exchange.AddOnCompletion(pending);
                }
            }

            await AddToQueueAsync(copy, exchange).ConfigureAwait(false);
        }
    }

    private async Task SendAndWaitAsync(Exchange exchange)
    {
        var copy = PrepareCopy(exchange);
        var reply = new TaskCompletionSource<Exchange>(TaskCreationOptions.RunContinuationsAsynchronously);
        copy.AddOnCompletion(new SedaReplySynchronization(reply));

        // A standalone source (not linked, so nothing to dispose): cancelled when the caller gives up, so that the
        // copy is skipped if still queued and its processing can observe the cancellation.
        var abandon = new CancellationTokenSource();
        copy.CancellationToken = abandon.Token;
        await AddToQueueAsync(copy, exchange).ConfigureAwait(false);

        var timeout = endpoint.Timeout;
        try
        {
            var response = timeout > TimeSpan.Zero
                ? await reply.Task.WaitAsync(timeout, endpoint.Context.TimeProvider, exchange.CancellationToken).ConfigureAwait(false)
                : await reply.Task.WaitAsync(exchange.CancellationToken).ConfigureAwait(false);
            exchange.CopyResultsFrom(response);
        }
        catch (TimeoutException)
        {
            await abandon.CancelAsync().ConfigureAwait(false);
            exchange.Exception = new ExchangeTimedOutException(exchange, timeout);
        }
        catch (OperationCanceledException)
        {
            await abandon.CancelAsync().ConfigureAwait(false);
            throw;
        }
    }

    private Exchange PrepareCopy(Exchange exchange)
    {
        var copy = exchange.CreateCorrelatedCopy();
        copy.FromEndpoint = endpoint;
        copy.FromRouteId = null;
        copy.CurrentRouteId = null;
        copy.CancellationToken = default;
        return copy;
    }

    private async Task AddToQueueAsync(Exchange copy, Exchange original)
    {
        var writer = endpoint.Queue.Channel.Writer;
        if (writer.TryWrite(copy))
        {
            return;
        }

        if (endpoint.DiscardWhenFull)
        {
            _logger.LogDebug("Discarding exchange {ExchangeId} as queue {Uri} is full", original.ExchangeId, endpoint.EndpointUri);
            return;
        }

        if (!endpoint.BlockWhenFull)
        {
            throw new InvalidOperationException($"Queue full: {endpoint.Queue.Key} (size {endpoint.Size})");
        }

        var offerTimeout = endpoint.OfferTimeout;
        if (offerTimeout <= TimeSpan.Zero)
        {
            await writer.WriteAsync(copy, original.CancellationToken).ConfigureAwait(false);
            return;
        }

        // Cancel the write itself on timeout so that it cannot complete later.
        using var timeoutSource = new CancellationTokenSource(offerTimeout, endpoint.Context.TimeProvider);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(timeoutSource.Token, original.CancellationToken);
        try
        {
            await writer.WriteAsync(copy, linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (timeoutSource.IsCancellationRequested && !original.CancellationToken.IsCancellationRequested)
        {
            throw new ExchangeTimedOutException(original, offerTimeout, "Fails to add the exchange to the queue");
        }
    }
}
