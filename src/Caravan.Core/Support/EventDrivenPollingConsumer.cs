using System.Threading.Channels;
using Caravan.Spi;

namespace Caravan.Support;

/// <summary>
/// Turns any event-driven endpoint into a polling one (Camel's <c>EventDrivenPollingConsumer</c>): it starts a regular
/// consumer of the endpoint whose processor puts the exchanges in an internal queue, from which
/// <see cref="IPollingConsumer"/> callers take them. Messages arriving while nobody receives are kept in the queue.
/// </summary>
public class EventDrivenPollingConsumer : ServiceSupport, IPollingConsumer, IProcessor
{
    private readonly Channel<Exchange> _queue;
    private IConsumer? _consumer;

    /// <summary>Creates the consumer.</summary>
    /// <param name="endpoint">The endpoint to consume from.</param>
    /// <param name="queueSize">The queue capacity (default 1000); zero or negative for unbounded.</param>
    /// <param name="blockWhenFull">Whether a full queue makes the event-driven consumer wait (default true) instead of failing.</param>
    /// <param name="blockTimeout">How long to wait when the queue is full; zero (default) waits indefinitely.</param>
    public EventDrivenPollingConsumer(IEndpoint endpoint, int queueSize = 1000, bool blockWhenFull = true, TimeSpan blockTimeout = default)
    {
        Endpoint = endpoint;
        QueueSize = queueSize;
        BlockWhenFull = blockWhenFull;
        BlockTimeout = blockTimeout;
        _queue = queueSize > 0
            ? Channel.CreateBounded<Exchange>(new BoundedChannelOptions(queueSize) { FullMode = BoundedChannelFullMode.Wait })
            : Channel.CreateUnbounded<Exchange>();
    }

    /// <inheritdoc />
    public IEndpoint Endpoint { get; }

    /// <summary>The queue capacity; zero or negative for unbounded.</summary>
    public int QueueSize { get; }

    /// <summary>Whether a full queue makes the event-driven consumer wait instead of failing.</summary>
    public bool BlockWhenFull { get; }

    /// <summary>How long to wait when the queue is full; zero waits indefinitely.</summary>
    public TimeSpan BlockTimeout { get; }

    /// <summary>The number of exchanges waiting to be received.</summary>
    public int Count => _queue.Reader.Count;

    /// <inheritdoc />
    public Task<Exchange> ReceiveAsync(CancellationToken cancellationToken = default) =>
        ChannelReceiver.ReceiveAsync(_queue.Reader, null, cancellationToken);

    /// <inheritdoc />
    public Task<Exchange?> ReceiveAsync(TimeSpan timeout, CancellationToken cancellationToken = default) =>
        ChannelReceiver.ReceiveAsync(_queue.Reader, null, timeout, Endpoint.Context.TimeProvider, cancellationToken);

    /// <inheritdoc />
    public Exchange? ReceiveNoWait() => ChannelReceiver.TryTake(_queue.Reader, null);

    /// <summary>Called by the event-driven consumer: queues the exchange.</summary>
    /// <exception cref="InvalidOperationException">The queue is full and <see cref="BlockWhenFull"/> is false.</exception>
    /// <exception cref="ExchangeTimedOutException">The queue stayed full for <see cref="BlockTimeout"/>.</exception>
    public async ValueTask ProcessAsync(Exchange exchange)
    {
        if (_queue.Writer.TryWrite(exchange))
        {
            return;
        }

        if (!BlockWhenFull)
        {
            throw new InvalidOperationException("Queue full");
        }

        if (BlockTimeout <= TimeSpan.Zero)
        {
            await _queue.Writer.WriteAsync(exchange, exchange.CancellationToken).ConfigureAwait(false);
            return;
        }

        try
        {
            await _queue.Writer.WriteAsync(exchange, exchange.CancellationToken).AsTask()
                .WaitAsync(BlockTimeout, Endpoint.Context.TimeProvider, exchange.CancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            throw new ExchangeTimedOutException(exchange, BlockTimeout, "Fails to add the exchange to the queue");
        }
    }

    /// <summary>Creates and starts the endpoint's event-driven consumer.</summary>
    protected override async Task DoStartAsync(CancellationToken cancellationToken)
    {
        await Endpoint.StartAsync(cancellationToken).ConfigureAwait(false);
        _consumer = Endpoint.CreateConsumer(this);
        await _consumer.StartAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Stops the event-driven consumer; queued exchanges are kept.</summary>
    protected override async Task DoStopAsync(CancellationToken cancellationToken)
    {
        if (_consumer is not null)
        {
            await _consumer.StopAsync(cancellationToken).ConfigureAwait(false);
            _consumer = null;
        }
    }

    /// <inheritdoc />
    public override string ToString() => $"EventDrivenPollingConsumer[{Endpoint.EndpointUri}]";
}
