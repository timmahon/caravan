using Caravan.Model;
using Caravan.Spi;
using Caravan.Support;

namespace Caravan.Processors;

/// <summary>
/// The <c>throttle</c> EIP: limits the exchanges passing through, per correlation key (<see cref="CorrelationExpression"/>; one shared limit
/// without it or when the key is null), in one of two modes (<see cref="ThrottlingMode"/>):
/// <list type="bullet">
/// <item><see cref="ThrottlingMode.ConcurrentRequests"/> (Camel 4's throttler): at most <see cref="MaxRequests"/> exchanges in flight; the permit is
/// returned when the exchange completes (its unit of work is done), so the limit covers the rest of the route. Waiting exchanges are let through
/// in arrival order.</item>
/// <item><see cref="ThrottlingMode.TotalRequests"/> (Camel 3's throttler): at most <see cref="MaxRequests"/> exchanges per <see cref="TimePeriod"/>,
/// over a sliding window: an exchange waits until the oldest admission in the window is one period old (measured on the context's
/// <see cref="ICaravanContext.TimeProvider"/>).</item>
/// </list>
/// </summary>
/// <remarks>
/// <see cref="MaxRequests"/> is evaluated for each exchange and the latest value applies to its key (Camel's dynamic throttling); a null or non-positive
/// value fails the exchange. With <see cref="RejectExecution"/> an exchange over the limit fails with a <see cref="ThrottlerRejectedExecutionException"/> instead of
/// waiting. Waiting ends with an <see cref="OperationCanceledException"/> when the exchange's <see cref="Exchange.CancellationToken"/> is cancelled.
/// Without <see cref="AsyncDelayed"/> the waiting thread is blocked (as Camel's synchronous throttler). <see cref="CallerRunsWhenRejected"/> is
/// accepted for parity but has no effect (there is no executor). State of correlation keys that are idle is discarded.
/// </remarks>
public sealed class ThrottleProcessor : IProcessor
{
    private const int SweepThreshold = 1024;
    private readonly Lock _lock = new();
    private readonly Dictionary<string, KeyState> _states = new(StringComparer.Ordinal);
    private readonly KeyState _defaultState = new(null);
    private int _sweepAt = SweepThreshold;

    /// <summary>Creates the processor.</summary>
    /// <param name="maxRequests">Computes the maximum number of requests (concurrent, or per period).</param>
    /// <param name="mode">The throttling mode.</param>
    /// <param name="timePeriod">The period of <see cref="ThrottlingMode.TotalRequests"/>.</param>
    /// <param name="correlationExpression">Computes the key that exchanges are throttled by; null for one shared limit.</param>
    /// <param name="rejectExecution">Fail exchanges over the limit instead of making them wait.</param>
    /// <param name="asyncDelayed">Wait without blocking the calling thread.</param>
    /// <param name="callerRunsWhenRejected">Accepted for parity with Camel; has no effect.</param>
    public ThrottleProcessor(
        IExpression maxRequests,
        ThrottlingMode mode = ThrottlingMode.ConcurrentRequests,
        TimeSpan? timePeriod = null,
        IExpression? correlationExpression = null,
        bool rejectExecution = false,
        bool asyncDelayed = true,
        bool callerRunsWhenRejected = true)
    {
        var period = timePeriod ?? TimeSpan.FromSeconds(1);
        if (period <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timePeriod), "The time period must be positive");
        }

        MaxRequests = maxRequests;
        Mode = mode;
        TimePeriod = period;
        CorrelationExpression = correlationExpression;
        RejectExecution = rejectExecution;
        AsyncDelayed = asyncDelayed;
        CallerRunsWhenRejected = callerRunsWhenRejected;
    }

    /// <summary>Computes the maximum number of requests (concurrent, or per period).</summary>
    public IExpression MaxRequests { get; }

    /// <summary>The throttling mode.</summary>
    public ThrottlingMode Mode { get; }

    /// <summary>The period of <see cref="ThrottlingMode.TotalRequests"/>.</summary>
    public TimeSpan TimePeriod { get; }

    /// <summary>Computes the key that exchanges are throttled by; null for one shared limit.</summary>
    public IExpression? CorrelationExpression { get; }

    /// <summary>Whether exchanges over the limit fail instead of waiting.</summary>
    public bool RejectExecution { get; }

    /// <summary>Whether waiting does not block the calling thread.</summary>
    public bool AsyncDelayed { get; }

    /// <summary>Accepted for parity with Camel; has no effect.</summary>
    public bool CallerRunsWhenRejected { get; }

    /// <summary>The number of exchanges currently holding a permit in <see cref="ThrottlingMode.ConcurrentRequests"/> mode, over all keys.</summary>
    public int InFlight
    {
        get
        {
            lock (_lock)
            {
                return _defaultState.InFlight + _states.Values.Sum(s => s.InFlight);
            }
        }
    }

    /// <summary>The number of exchanges waiting for a permit in <see cref="ThrottlingMode.ConcurrentRequests"/> mode, over all keys.</summary>
    public int Waiting
    {
        get
        {
            lock (_lock)
            {
                return _defaultState.Waiters.Count + _states.Values.Sum(s => s.Waiters.Count);
            }
        }
    }

    /// <summary>The number of correlation keys with throttling state.</summary>
    public int KeyCount
    {
        get
        {
            lock (_lock)
            {
                return _states.Count;
            }
        }
    }

    /// <inheritdoc />
    public async ValueTask ProcessAsync(Exchange exchange)
    {
        var value = MaxRequests.Evaluate(exchange)
            ?? throw new CaravanExchangeException("The max requests expression was evaluated as null", exchange);
        var max = (long)exchange.Context.TypeConverter.Convert(typeof(long), exchange, value)!;
        if (max <= 0)
        {
            throw new CaravanExchangeException($"The max requests must be a positive number, was: {max}", exchange);
        }

        var key = CorrelationExpression is null ? null : ObjectHelper.ToText(exchange, CorrelationExpression.Evaluate(exchange));
        if (Mode == ThrottlingMode.ConcurrentRequests)
        {
            await AcquireConcurrentAsync(exchange, key, max).ConfigureAwait(false);
        }
        else
        {
            await AcquireTotalAsync(exchange, key, max).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public override string ToString() => $"Throttle[{Mode} {MaxRequests}]";

    private async ValueTask AcquireConcurrentAsync(Exchange exchange, string? key, long max)
    {
        KeyState state;
        TaskCompletionSource? waiter = null;
        lock (_lock)
        {
            state = GetState(key, exchange.Context.TimeProvider.GetUtcNow());
            state.Max = max;
            Drain(state);
            if (state.Waiters.Count == 0 && state.InFlight < max)
            {
                state.InFlight++;
            }
            else if (RejectExecution)
            {
                RemoveIfIdle(state);
                throw new ThrottlerRejectedExecutionException($"Exceeded the max concurrent requests of {max}", exchange);
            }
            else
            {
                waiter = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                state.Waiters.AddLast(waiter);
            }
        }

        if (waiter is not null)
        {
            var token = exchange.CancellationToken;
            using (token.Register(() => Cancel(state, waiter, token)))
            {
                if (AsyncDelayed)
                {
                    await waiter.Task.ConfigureAwait(false);
                }
                else
                {
                    waiter.Task.GetAwaiter().GetResult();
                }
            }
        }

        exchange.AddOnCompletion(new Permit(this, state));
    }

    private async ValueTask AcquireTotalAsync(Exchange exchange, string? key, long max)
    {
        var time = exchange.Context.TimeProvider;
        while (true)
        {
            TimeSpan wait;
            lock (_lock)
            {
                var now = time.GetUtcNow();
                var state = GetState(key, now);
                state.Max = max;
                while (state.Admissions.Count > 0 && state.Admissions.Peek() + TimePeriod <= now)
                {
                    state.Admissions.Dequeue();
                }

                if (state.Admissions.Count < max)
                {
                    state.Admissions.Enqueue(now);
                    return;
                }

                if (RejectExecution)
                {
                    throw new ThrottlerRejectedExecutionException(
                        $"Exceeded the max throttle rate of {max} within {TimePeriod.TotalMilliseconds}ms",
                        exchange);
                }

                // Enough admissions must leave the window for this exchange to fit (more than one when the maximum decreased).
                var expiring = state.Admissions.ElementAt((int)(state.Admissions.Count - max));
                wait = expiring + TimePeriod - now;
            }

            await ExchangeHelper.SleepAsync(wait, time, AsyncDelayed, exchange.CancellationToken).ConfigureAwait(false);
        }
    }

    private KeyState GetState(string? key, DateTimeOffset now)
    {
        if (key is null)
        {
            return _defaultState;
        }

        if (!_states.TryGetValue(key, out var state))
        {
            if (_states.Count >= _sweepAt)
            {
                Sweep(now);
            }

            state = new KeyState(key);
            _states[key] = state;
        }

        return state;
    }

    private void Sweep(DateTimeOffset now)
    {
        foreach (var (key, state) in _states.ToList())
        {
            if (state.InFlight == 0 && state.Waiters.Count == 0 && state.Admissions.All(admitted => admitted + TimePeriod <= now))
            {
                _states.Remove(key);
            }
        }

        _sweepAt = Math.Max(SweepThreshold, _states.Count * 2);
    }

    private void RemoveIfIdle(KeyState state)
    {
        if (state.Key is not null && state.InFlight == 0 && state.Waiters.Count == 0 && state.Admissions.Count == 0)
        {
            _states.Remove(state.Key);
        }
    }

    private static void Drain(KeyState state)
    {
        while (state.Waiters.First is { } first && state.InFlight < state.Max)
        {
            state.Waiters.RemoveFirst();
            if (first.Value.TrySetResult())
            {
                state.InFlight++;
            }
        }
    }

    private void Cancel(KeyState state, TaskCompletionSource waiter, CancellationToken token)
    {
        lock (_lock)
        {
            // Only a waiter still queued is cancelled; one already let through keeps its permit (released on completion).
            if (state.Waiters.Remove(waiter))
            {
                waiter.TrySetCanceled(token);
                RemoveIfIdle(state);
            }
        }
    }

    private void Release(KeyState state)
    {
        lock (_lock)
        {
            state.InFlight--;
            Drain(state);
            RemoveIfIdle(state);
        }
    }

    /// <summary>Throttling state of one correlation key.</summary>
    private sealed class KeyState(string? key)
    {
        public string? Key { get; } = key;

        public long Max { get; set; }

        public int InFlight { get; set; }

        public LinkedList<TaskCompletionSource> Waiters { get; } = new();

        public Queue<DateTimeOffset> Admissions { get; } = new();
    }

    /// <summary>Returns a concurrent-requests permit when the exchange completes; never handed over to another exchange.</summary>
    private sealed class Permit(ThrottleProcessor owner, KeyState state) : ISynchronization
    {
        private int _released;

        public bool AllowHandover => false;

        public ValueTask OnCompleteAsync(Exchange exchange)
        {
            ReleaseOnce();
            return ValueTask.CompletedTask;
        }

        public ValueTask OnFailureAsync(Exchange exchange)
        {
            ReleaseOnce();
            return ValueTask.CompletedTask;
        }

        private void ReleaseOnce()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                owner.Release(state);
            }
        }
    }
}
