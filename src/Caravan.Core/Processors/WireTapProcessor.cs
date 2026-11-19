using System.Collections.Concurrent;
using Caravan.Spi;
using Caravan.Support;
using Microsoft.Extensions.Logging;

namespace Caravan.Processors;

/// <summary>
/// The <c>wireTap</c> EIP: sends a copy of the exchange to an endpoint on the thread pool as an event message
/// (<c>InOnly</c>) while the original continues immediately. The tap URI is evaluated on the copy, after
/// <see cref="OnPrepare"/>. Failures of the tap are logged and never affect the original. Stopping the processor
/// waits for outstanding taps.
/// </summary>
public sealed class WireTapProcessor : ServiceSupport, IProcessor
{
    private readonly ConcurrentDictionary<long, Task> _inflight = new();
    private long _sequence;

    /// <summary>Creates the processor.</summary>
    /// <param name="sender">Sends the tapped exchange; it is started and stopped with this processor.</param>
    /// <param name="copy">
    /// True (default) to tap a correlated copy of the exchange; false to tap a new, empty exchange (as in Camel).
    /// </param>
    /// <param name="onPrepare">Optional callback to modify the tapped exchange before it is sent.</param>
    public WireTapProcessor(SendDynamicProcessor sender, bool copy = true, Action<Exchange>? onPrepare = null)
    {
        Sender = sender;
        Copy = copy;
        OnPrepare = onPrepare;
    }

    /// <summary>Sends the tapped exchanges.</summary>
    public SendDynamicProcessor Sender { get; }

    /// <summary>Whether the tapped exchange is a copy of the original (true) or a new empty exchange (false).</summary>
    public bool Copy { get; }

    /// <summary>Called on the tapped exchange, on the caller's thread, before it is sent.</summary>
    public Action<Exchange>? OnPrepare { get; }

    /// <summary>The number of taps still being sent.</summary>
    public int PendingCount => _inflight.Count;

    /// <inheritdoc />
    public ValueTask ProcessAsync(Exchange exchange)
    {
        var tap = Copy
            ? exchange.CreateCorrelatedCopy()
            : new Exchange(exchange.Context) { FromEndpoint = exchange.FromEndpoint, FromRouteId = exchange.FromRouteId };
        tap.Pattern = ExchangePattern.InOnly;
        tap.CancellationToken = exchange.Context.ShutdownToken;
        OnPrepare?.Invoke(tap);

        var id = Interlocked.Increment(ref _sequence);
        var task = Task.Run(async () =>
        {
            try
            {
                await SendAsync(tap).ConfigureAwait(false);
            }
            finally
            {
                _inflight.TryRemove(id, out _);
            }
        });
        _inflight.TryAdd(id, task);
        if (task.IsCompleted)
        {
            _inflight.TryRemove(id, out _);
        }

        return ValueTask.CompletedTask;
    }

    /// <summary>Starts the sender.</summary>
    protected override Task DoStartAsync(CancellationToken cancellationToken) => Sender.StartAsync(cancellationToken);

    /// <summary>Waits for outstanding taps, then stops the sender.</summary>
    protected override async Task DoStopAsync(CancellationToken cancellationToken)
    {
        while (!_inflight.IsEmpty)
        {
            await Task.WhenAll(_inflight.Values).WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        await Sender.StopAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public override string ToString() => $"WireTap[{Sender.Uri}]";

    private async Task SendAsync(Exchange tap)
    {
        try
        {
            await Sender.ProcessAsync(tap).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            tap.Exception = e;
        }

        if (tap.Exception is { } failure)
        {
            tap.Context.LoggerFactory.CreateLogger<WireTapProcessor>()
                .LogWarning(failure, "Wire tap to {Uri} failed for exchange {ExchangeId}", Sender.Uri, tap.ExchangeId);
        }
    }
}
