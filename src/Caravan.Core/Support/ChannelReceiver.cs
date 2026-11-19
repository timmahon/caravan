using System.Threading.Channels;

namespace Caravan.Support;

/// <summary>Reads exchanges from a channel for polling consumers, with optional timeout and filtering.</summary>
internal static class ChannelReceiver
{
    /// <summary>Takes the next accepted exchange, waiting as long as needed.</summary>
    public static async Task<Exchange> ReceiveAsync(ChannelReader<Exchange> reader, Func<Exchange, bool>? accept, CancellationToken cancellationToken)
    {
        while (true)
        {
            if (!await reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
            {
                throw new OperationCanceledException("The queue was closed");
            }

            if (TryTake(reader, accept) is { } exchange)
            {
                return exchange;
            }
        }
    }

    /// <summary>Takes the next accepted exchange, waiting up to <paramref name="timeout"/>; null when none arrived.</summary>
    public static async Task<Exchange?> ReceiveAsync(
        ChannelReader<Exchange> reader,
        Func<Exchange, bool>? accept,
        TimeSpan timeout,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (TryTake(reader, accept) is { } ready)
        {
            return ready;
        }

        if (timeout <= TimeSpan.Zero)
        {
            return null;
        }

        // Cancel the read itself on timeout (rather than abandoning it), so that no message is taken and dropped.
        using var timeoutSource = new CancellationTokenSource(timeout, timeProvider);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(timeoutSource.Token, cancellationToken);
        try
        {
            return await ReceiveAsync(reader, accept, linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (timeoutSource.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    /// <summary>Takes an accepted exchange if one is available right away.</summary>
    public static Exchange? TryTake(ChannelReader<Exchange> reader, Func<Exchange, bool>? accept)
    {
        while (reader.TryRead(out var exchange))
        {
            if (accept?.Invoke(exchange) ?? true)
            {
                return exchange;
            }
        }

        return null;
    }
}
