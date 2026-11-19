using System.Threading.Channels;
using Caravan.Model;
using Caravan.Spi;
using Caravan.Support;
using Microsoft.Extensions.Logging;

namespace Caravan.Processors;

/// <summary>
/// The batch <c>resequence</c> EIP (Camel's <c>Resequencer</c>): collects copies of the incoming exchanges into a batch, which is
/// emitted, sorted by the expression, when it holds <see cref="BatchResequencerConfig.BatchSize"/> exchanges or
/// <see cref="BatchResequencerConfig.BatchTimeout"/> after its first exchange arrived. Batches are sent one exchange at a time, in order,
/// by a single background sender; the incoming exchanges continue at once.
/// </summary>
/// <remarks>
/// <para>
/// Sort keys of different types are compared with type coercion (<see cref="ObjectHelper.TypeCoerceCompare"/>). Without
/// <see cref="BatchResequencerConfig.AllowDuplicates"/>, only the first exchange (in arrival order) of equal keys is kept. An exchange
/// whose expression fails or yields null is invalid: it fails with a <see cref="CaravanExchangeException"/>, or is dropped with
/// <see cref="ResequencerConfig.IgnoreInvalidExchanges"/>.
/// </para>
/// <para>
/// When the route stops gracefully the current batch is emitted at once and sent before the processor stops. Difference from Camel:
/// the timeout counts from the batch's first exchange (Camel restarts it on each arrival), so a steady trickle cannot hold a batch back.
/// </para>
/// </remarks>
public sealed class BatchResequencerProcessor : ServiceSupport, INavigableProcessor, IShutdownAware
{
    private readonly Lock _lock = new();
    private readonly List<(Exchange Exchange, object Key)> _batch = [];
    private readonly DetachedExchangeSender _sender;
    private Channel<List<Exchange>> _ready = System.Threading.Channels.Channel.CreateUnbounded<List<Exchange>>(new UnboundedChannelOptions { SingleReader = true });
    private Task? _senderLoop;
    private ITimer? _timer;
    private long _generation;
    private int _queued;

    /// <summary>Creates the processor.</summary>
    /// <param name="context">The context.</param>
    /// <param name="processor">Processes the reordered exchanges (the EIP's nested block).</param>
    /// <param name="expression">Computes the sort key.</param>
    /// <param name="config">The options.</param>
    /// <param name="routeId">The id of the route.</param>
    /// <exception cref="ArgumentException">The batch size or timeout is not positive.</exception>
    public BatchResequencerProcessor(ICaravanContext context, IProcessor processor, IExpression expression, BatchResequencerConfig config, string routeId)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(processor);
        ArgumentNullException.ThrowIfNull(expression);
        ArgumentNullException.ThrowIfNull(config);
        if (config.BatchSize <= 0)
        {
            throw new ArgumentException("batchSize must be positive", nameof(config));
        }

        if (config.BatchTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentException("batchTimeout must be positive", nameof(config));
        }

        Context = context;
        Processor = processor;
        Expression = expression;
        Config = config;
        _sender = new DetachedExchangeSender(processor, routeId, "resequenced");
    }

    /// <summary>The context.</summary>
    public ICaravanContext Context { get; }

    /// <summary>Processes the reordered exchanges.</summary>
    public IProcessor Processor { get; }

    /// <summary>Computes the sort key.</summary>
    public IExpression Expression { get; }

    /// <summary>The options.</summary>
    public BatchResequencerConfig Config { get; }

    /// <inheritdoc />
    public IReadOnlyList<IProcessor> Next => [Processor];

    /// <summary>The exchanges accepted and not sent yet (in the current batch, waiting to be sent, or being sent).</summary>
    public int PendingExchangesSize => Volatile.Read(ref _queued);

    /// <inheritdoc />
    public ValueTask ProcessAsync(Exchange exchange)
    {
        object? key;
        Exception? cause = null;
        try
        {
            key = Expression.Evaluate(exchange);
        }
        catch (Exception e)
        {
            key = null;
            cause = e;
        }

        if (key is null)
        {
            if (Config.IgnoreInvalidExchanges)
            {
                Context.LoggerFactory.CreateLogger<BatchResequencerProcessor>().LogDebug(cause, "Invalid exchange {ExchangeId} is ignored", exchange.ExchangeId);
                return ValueTask.CompletedTask;
            }

            throw new CaravanExchangeException("Exchange is not valid to be used by the BatchProcessor", exchange, cause);
        }

        if (!IsRunAllowed)
        {
            throw new CaravanExchangeException("The resequencer is not started", exchange);
        }

        var copy = exchange.CreateCorrelatedCopy();
        List<Exchange>? ready = null;
        lock (_lock)
        {
            _batch.Add((copy, key));
            Interlocked.Increment(ref _queued);
            if (_batch.Count >= Config.BatchSize)
            {
                ready = TakeBatch();
            }
            else if (_batch.Count == 1)
            {
                _timer = Context.TimeProvider.CreateTimer(OnTimeout, ++_generation, Config.BatchTimeout, Timeout.InfiniteTimeSpan);
            }
        }

        Emit(ready);
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public bool DeferShutdown(ShutdownRunningTask runningTask) => false;

    /// <summary>When the route is being stopped gracefully, emits the current batch at once.</summary>
    /// <param name="runningTask">The route's <see cref="ShutdownRunningTask"/>.</param>
    /// <param name="suspendOnly">True when the route is being suspended.</param>
    /// <param name="forced">True when the shutdown timed out.</param>
    public void PrepareShutdown(ShutdownRunningTask runningTask, bool suspendOnly, bool forced)
    {
        if (!forced && !suspendOnly)
        {
            Flush();
        }
    }

    /// <inheritdoc />
    public override string ToString() => $"Resequence[batch {Expression} -> {Processor}]";

    /// <summary>Starts the sender.</summary>
    protected override Task DoStartAsync(CancellationToken cancellationToken)
    {
        _ready = System.Threading.Channels.Channel.CreateUnbounded<List<Exchange>>(new UnboundedChannelOptions { SingleReader = true });
        var reader = _ready.Reader;
        _senderLoop = Task.Run(() => SendLoopAsync(reader), CancellationToken.None);
        return Task.CompletedTask;
    }

    /// <summary>Emits the current batch and waits until everything accepted was sent.</summary>
    protected override async Task DoStopAsync(CancellationToken cancellationToken)
    {
        Flush();
        _ready.Writer.TryComplete();
        if (_senderLoop is { } loop)
        {
            await loop.WaitAsync(cancellationToken).ConfigureAwait(false);
            _senderLoop = null;
        }
    }

    private void Flush()
    {
        List<Exchange>? ready = null;
        lock (_lock)
        {
            if (_batch.Count > 0)
            {
                ready = TakeBatch();
            }
        }

        Emit(ready);
    }

    private void OnTimeout(object? state)
    {
        List<Exchange>? ready = null;
        lock (_lock)
        {
            if ((long)state! == _generation && _batch.Count > 0)
            {
                ready = TakeBatch();
            }
        }

        Emit(ready);
    }

    private void Emit(List<Exchange>? ready)
    {
        if (ready is not null && !_ready.Writer.TryWrite(ready))
        {
            Interlocked.Add(ref _queued, -ready.Count);
            Context.LoggerFactory.CreateLogger<BatchResequencerProcessor>().LogWarning("The resequencer is stopped; {Count} exchanges are dropped", ready.Count);
        }
    }

    /// <summary>Sorts and takes the current batch (called under the lock).</summary>
    private List<Exchange> TakeBatch()
    {
        _timer?.Dispose();
        _timer = null;
        _generation++;
        var reference = _batch[0].Exchange;
        var comparer = Comparer<object>.Create((a, b) => ObjectHelper.TypeCoerceCompare(reference, a, b));
        List<(Exchange Exchange, object Key)> sorted;
        try
        {
            sorted = [.. Config.Reverse ? _batch.OrderByDescending(e => e.Key, comparer) : _batch.OrderBy(e => e.Key, comparer)];
        }
        catch (Exception e) when (e is InvalidOperationException or ArgumentException)
        {
            // Keys that cannot be compared: the batch goes out in arrival order rather than being lost.
            Context.LoggerFactory.CreateLogger<BatchResequencerProcessor>().LogWarning(e, "Cannot sort the batch; it is sent in arrival order");
            sorted = [.. _batch];
            comparer = Comparer<object>.Create((a, b) => Equals(a, b) ? 0 : 1);
        }

        var result = new List<Exchange>(_batch.Count);
        object? previous = null;
        foreach (var (exchange, key) in sorted)
        {
            if (!Config.AllowDuplicates && result.Count > 0 && comparer.Compare(previous, key) == 0)
            {
                continue;
            }

            result.Add(exchange);
            previous = key;
        }

        Interlocked.Add(ref _queued, result.Count - _batch.Count);
        _batch.Clear();
        return result;
    }

    private async Task SendLoopAsync(ChannelReader<List<Exchange>> reader)
    {
        await foreach (var batch in reader.ReadAllAsync().ConfigureAwait(false))
        {
            foreach (var exchange in batch)
            {
                try
                {
                    await _sender.SendAsync(exchange).ConfigureAwait(false);
                }
                finally
                {
                    Interlocked.Decrement(ref _queued);
                }
            }
        }
    }
}
