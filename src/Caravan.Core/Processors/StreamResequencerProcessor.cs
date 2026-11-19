using Caravan.Model;
using Caravan.Spi;
using Caravan.Support;
using Microsoft.Extensions.Logging;

namespace Caravan.Processors;

/// <summary>
/// The stream <c>resequence</c> EIP (Camel's <c>StreamResequencer</c> and <c>ResequencerEngine</c>): exchanges carry consecutive
/// sequence numbers (a <see cref="long"/> computed by the expression) and are delivered in order, one at a time, as soon as they are in
/// sequence; the incoming exchanges continue at once.
/// </summary>
/// <remarks>
/// <para>
/// An exchange is held back while it has a gap before it: when it is neither the successor of the last delivered exchange nor has its
/// predecessor waiting, a timer of <see cref="StreamResequencerConfig.Timeout"/> starts; when the predecessor arrives the timer is
/// cancelled, and when it elapses the gap is given up on. As in Camel, the very first exchange waits for its timeout (there is no
/// last delivered exchange yet) unless the gap before it is filled. Exchanges with a sequence number already waiting are dropped;
/// late exchanges (lower than the last delivered one) are delivered after their timeout, or fail with a
/// <see cref="MessageRejectedException"/> with <see cref="StreamResequencerConfig.RejectOld"/>.
/// </para>
/// <para>
/// At most <see cref="StreamResequencerConfig.Capacity"/> exchanges are held: senders wait for room. Invalid exchanges (the expression
/// fails, yields null or a value that is not a number) fail with a <see cref="CaravanExchangeException"/>, or are dropped with
/// <see cref="ResequencerConfig.IgnoreInvalidExchanges"/>. Difference from Camel: when the route stops gracefully the held exchanges are
/// delivered in order without waiting for the gaps (Camel drops them).
/// </para>
/// </remarks>
public sealed class StreamResequencerProcessor : ServiceSupport, INavigableProcessor, IShutdownAware
{
    private readonly Lock _lock = new();
    private readonly SortedDictionary<long, Element> _sequence = [];
    private readonly SemaphoreSlim _deliveryRequests = new(0);
    private readonly DetachedExchangeSender _sender;
    private long? _lastDelivered;
    private TaskCompletionSource? _space;
    private CancellationTokenSource? _stop;
    private Task? _deliveryLoop;
    private bool _flushing;
    private int _signaled;
    private int _sending;

    /// <summary>Creates the processor.</summary>
    /// <param name="context">The context.</param>
    /// <param name="processor">Processes the reordered exchanges (the EIP's nested block).</param>
    /// <param name="expression">Computes the sequence number.</param>
    /// <param name="config">The options.</param>
    /// <param name="routeId">The id of the route.</param>
    /// <exception cref="ArgumentException">The capacity, timeout or delivery attempt interval is not positive.</exception>
    public StreamResequencerProcessor(ICaravanContext context, IProcessor processor, IExpression expression, StreamResequencerConfig config, string routeId)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(processor);
        ArgumentNullException.ThrowIfNull(expression);
        ArgumentNullException.ThrowIfNull(config);
        if (config.Capacity <= 0)
        {
            throw new ArgumentException("capacity must be positive", nameof(config));
        }

        if (config.Timeout <= TimeSpan.Zero || config.DeliveryAttemptInterval <= TimeSpan.Zero)
        {
            throw new ArgumentException("timeout and deliveryAttemptInterval must be positive", nameof(config));
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

    /// <summary>Computes the sequence number.</summary>
    public IExpression Expression { get; }

    /// <summary>The options.</summary>
    public StreamResequencerConfig Config { get; }

    /// <inheritdoc />
    public IReadOnlyList<IProcessor> Next => [Processor];

    /// <summary>The sequence number of the last delivered exchange, if any.</summary>
    public long? LastDelivered
    {
        get
        {
            lock (_lock)
            {
                return _lastDelivered;
            }
        }
    }

    /// <summary>The exchanges held or being sent.</summary>
    public int PendingExchangesSize
    {
        get
        {
            lock (_lock)
            {
                return _sequence.Count + _sending;
            }
        }
    }

    /// <inheritdoc />
    public async ValueTask ProcessAsync(Exchange exchange)
    {
        long sequence;
        try
        {
            var value = Expression.Evaluate(exchange) ?? throw new InvalidOperationException("The sequence number is null");
            sequence = (long)Context.TypeConverter.Convert(typeof(long), exchange, value)!;
        }
        catch (Exception e)
        {
            if (Config.IgnoreInvalidExchanges)
            {
                Context.LoggerFactory.CreateLogger<StreamResequencerProcessor>().LogDebug(e, "Invalid exchange {ExchangeId} is ignored", exchange.ExchangeId);
                return;
            }

            throw new CaravanExchangeException("Error processing Exchange in StreamResequencer", exchange, e);
        }

        if (!IsRunAllowed)
        {
            throw new CaravanExchangeException("The resequencer is not started", exchange);
        }

        var copy = exchange.CreateCorrelatedCopy();
        while (true)
        {
            Task room;
            lock (_lock)
            {
                if (_sequence.Count < Config.Capacity)
                {
                    Insert(sequence, copy, exchange);
                    break;
                }

                room = (_space ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)).Task;
            }

            await room.WaitAsync(exchange.CancellationToken).ConfigureAwait(false);
        }

        RequestDelivery();
    }

    /// <inheritdoc />
    public bool DeferShutdown(ShutdownRunningTask runningTask) => false;

    /// <summary>When the route is being stopped gracefully, delivers the held exchanges in order without waiting for gaps.</summary>
    /// <param name="runningTask">The route's <see cref="ShutdownRunningTask"/>.</param>
    /// <param name="suspendOnly">True when the route is being suspended.</param>
    /// <param name="forced">True when the shutdown timed out.</param>
    public void PrepareShutdown(ShutdownRunningTask runningTask, bool suspendOnly, bool forced)
    {
        if (!forced && !suspendOnly)
        {
            lock (_lock)
            {
                _flushing = true;
            }

            RequestDelivery();
        }
    }

    /// <inheritdoc />
    public override string ToString() => $"Resequence[stream {Expression} -> {Processor}]";

    /// <summary>Starts the delivery task.</summary>
    protected override Task DoStartAsync(CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            _flushing = false;
        }

        _stop = new CancellationTokenSource();
        var token = _stop.Token;
        _deliveryLoop = Task.Run(() => DeliveryLoopAsync(token), CancellationToken.None);
        return Task.CompletedTask;
    }

    /// <summary>Delivers the held exchanges in order, then stops the delivery task.</summary>
    protected override async Task DoStopAsync(CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            _flushing = true;
        }

        RequestDelivery();
        if (_stop is { } stop)
        {
            await stop.CancelAsync().ConfigureAwait(false);
            if (_deliveryLoop is { } loop)
            {
                await loop.WaitAsync(cancellationToken).ConfigureAwait(false);
            }

            stop.Dispose();
            _stop = null;
            _deliveryLoop = null;
        }

        lock (_lock)
        {
            foreach (var element in _sequence.Values)
            {
                element.Cancel();
            }

            _sequence.Clear();
            _lastDelivered = null;
            _space?.TrySetResult();
            _space = null;
        }
    }

    /// <summary>Adds an exchange (called under the lock): Camel's <c>ResequencerEngine.insert</c>.</summary>
    private void Insert(long sequence, Exchange exchange, Exchange original)
    {
        if (Config.RejectOld && _lastDelivered is { } last && sequence < last)
        {
            throw new MessageRejectedException(
                $"Rejecting message with sequence number {sequence}, it should have been sent before the last delivered message {last}",
                original);
        }

        if (_sequence.ContainsKey(sequence))
        {
            Context.LoggerFactory.CreateLogger<StreamResequencerProcessor>().LogDebug(
                "Duplicate sequence number {Sequence}; exchange {ExchangeId} is dropped",
                sequence,
                original.ExchangeId);
            return;
        }

        var element = new Element(sequence, exchange);
        _sequence.Add(sequence, element);

        // The successor no longer waits for a gap to be filled.
        if (_sequence.TryGetValue(sequence + 1, out var successor))
        {
            successor.Cancel();
        }

        if (_lastDelivered != sequence - 1 && !_sequence.ContainsKey(sequence - 1))
        {
            element.Timer = Context.TimeProvider.CreateTimer(OnTimeout, element, Config.Timeout, Timeout.InfiniteTimeSpan);
        }
    }

    private void OnTimeout(object? state)
    {
        lock (_lock)
        {
            ((Element)state!).Cancel();
        }

        RequestDelivery();
    }

    private void RequestDelivery()
    {
        if (Interlocked.Exchange(ref _signaled, 1) == 0)
        {
            _deliveryRequests.Release();
        }
    }

    /// <summary>Takes the lowest exchange if it is not waiting for a gap (Camel's <c>deliverNext</c>).</summary>
    private bool TryTakeNext(out Exchange exchange)
    {
        lock (_lock)
        {
            exchange = null!;
            if (_sequence.Count == 0)
            {
                return false;
            }

            var first = _sequence.First().Value;
            if (first.Scheduled && !_flushing)
            {
                return false;
            }

            first.Cancel();
            _sequence.Remove(first.Sequence);
            _lastDelivered = first.Sequence;
            _sending++;
            _space?.TrySetResult();
            _space = null;
            exchange = first.Exchange;
            return true;
        }
    }

    private async Task DeliveryLoopAsync(CancellationToken token)
    {
        while (true)
        {
            Volatile.Write(ref _signaled, 0);
            while (TryTakeNext(out var exchange))
            {
                try
                {
                    await _sender.SendAsync(exchange).ConfigureAwait(false);
                }
                finally
                {
                    lock (_lock)
                    {
                        _sending--;
                    }
                }
            }

            if (token.IsCancellationRequested)
            {
                return;
            }

            using var attempt = new CancellationTokenSource(Config.DeliveryAttemptInterval, Context.TimeProvider);
            using var wake = CancellationTokenSource.CreateLinkedTokenSource(token, attempt.Token);
            try
            {
                await _deliveryRequests.WaitAsync(wake.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Periodic delivery attempt, or stopping (the loop delivers what is left first).
            }
        }
    }

    /// <summary>An exchange held by the resequencer.</summary>
    private sealed class Element(long sequence, Exchange exchange)
    {
        public long Sequence { get; } = sequence;

        public Exchange Exchange { get; } = exchange;

        public ITimer? Timer { get; set; }

        /// <summary>Whether the element waits for a gap before it to be filled.</summary>
        public bool Scheduled => Timer is not null;

        public void Cancel()
        {
            Timer?.Dispose();
            Timer = null;
        }
    }
}
