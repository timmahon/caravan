namespace Caravan.Management;

/// <summary>
/// Thread-safe, lock-free performance counters of a context, route or node (Camel's <c>ManagedPerformanceCounter</c>). Processing times are
/// recorded for completed exchanges only, as in Camel.
/// </summary>
internal sealed class ProcessingCounters
{
    private long _total;
    private long _completed;
    private long _failed;
    private long _inflight;
    private long _failuresHandled;
    private long _redeliveries;
    private long _minTicks = long.MaxValue;
    private long _maxTicks;
    private long _sumTicks;
    private long _lastTicks = -1;
    private long _firstCompleted;
    private long _lastCompleted;
    private long _lastFailed;
    private long _lastCreated;
    private long _resetAt;

    public long Total => Interlocked.Read(ref _total);

    public long Completed => Interlocked.Read(ref _completed);

    public long Failed => Interlocked.Read(ref _failed);

    public long Inflight => Interlocked.Read(ref _inflight);

    /// <summary>An exchange starts being processed.</summary>
    public void Begin(DateTimeOffset now)
    {
        Interlocked.Increment(ref _inflight);
        Interlocked.Increment(ref _total);
        Interlocked.Exchange(ref _lastCreated, now.UtcTicks);
    }

    /// <summary>An exchange finished being processed.</summary>
    public void End(TimeSpan elapsed, bool failed, DateTimeOffset now)
    {
        if (failed)
        {
            Interlocked.Increment(ref _failed);
            Interlocked.Exchange(ref _lastFailed, now.UtcTicks);
        }
        else
        {
            var ticks = Math.Max(0, elapsed.Ticks);
            Interlocked.Add(ref _sumTicks, ticks);
            Interlocked.Exchange(ref _lastTicks, ticks);
            UpdateMin(ticks);
            UpdateMax(ticks);
            Interlocked.CompareExchange(ref _firstCompleted, now.UtcTicks, 0);
            Interlocked.Exchange(ref _lastCompleted, now.UtcTicks);
            Interlocked.Increment(ref _completed);
        }

        Interlocked.Decrement(ref _inflight);
    }

    public void IncrementFailuresHandled() => Interlocked.Increment(ref _failuresHandled);

    public void IncrementRedeliveries() => Interlocked.Increment(ref _redeliveries);

    /// <summary>Resets the counters (inflight exchanges are kept).</summary>
    public void Reset(DateTimeOffset now)
    {
        Interlocked.Exchange(ref _total, Interlocked.Read(ref _inflight));
        Interlocked.Exchange(ref _completed, 0);
        Interlocked.Exchange(ref _failed, 0);
        Interlocked.Exchange(ref _failuresHandled, 0);
        Interlocked.Exchange(ref _redeliveries, 0);
        Interlocked.Exchange(ref _minTicks, long.MaxValue);
        Interlocked.Exchange(ref _maxTicks, 0);
        Interlocked.Exchange(ref _sumTicks, 0);
        Interlocked.Exchange(ref _lastTicks, -1);
        Interlocked.Exchange(ref _firstCompleted, 0);
        Interlocked.Exchange(ref _lastCompleted, 0);
        Interlocked.Exchange(ref _lastFailed, 0);
        Interlocked.Exchange(ref _lastCreated, 0);
        Interlocked.Exchange(ref _resetAt, now.UtcTicks);
    }

    public ProcessingStatistics Snapshot()
    {
        var completed = Completed;
        var sum = Interlocked.Read(ref _sumTicks);
        var min = Interlocked.Read(ref _minTicks);
        var last = Interlocked.Read(ref _lastTicks);
        return new ProcessingStatistics
        {
            ExchangesTotal = Total,
            ExchangesCompleted = completed,
            ExchangesFailed = Failed,
            ExchangesInflight = Inflight,
            FailuresHandled = Interlocked.Read(ref _failuresHandled),
            Redeliveries = Interlocked.Read(ref _redeliveries),
            MinProcessingTime = min == long.MaxValue ? null : TimeSpan.FromTicks(min),
            MaxProcessingTime = completed == 0 ? null : TimeSpan.FromTicks(Interlocked.Read(ref _maxTicks)),
            MeanProcessingTime = completed == 0 ? null : TimeSpan.FromTicks(sum / completed),
            LastProcessingTime = last < 0 ? null : TimeSpan.FromTicks(last),
            TotalProcessingTime = TimeSpan.FromTicks(sum),
            FirstExchangeCompletedTimestamp = ToTimestamp(ref _firstCompleted),
            LastExchangeCompletedTimestamp = ToTimestamp(ref _lastCompleted),
            LastExchangeFailureTimestamp = ToTimestamp(ref _lastFailed),
            LastExchangeCreatedTimestamp = ToTimestamp(ref _lastCreated),
            ResetTimestamp = ToTimestamp(ref _resetAt),
        };
    }

    private static DateTimeOffset? ToTimestamp(ref long ticks)
    {
        var value = Interlocked.Read(ref ticks);
        return value == 0 ? null : new DateTimeOffset(value, TimeSpan.Zero);
    }

    private void UpdateMin(long ticks)
    {
        var current = Interlocked.Read(ref _minTicks);
        while (ticks < current)
        {
            var seen = Interlocked.CompareExchange(ref _minTicks, ticks, current);
            if (seen == current)
            {
                return;
            }

            current = seen;
        }
    }

    private void UpdateMax(long ticks)
    {
        var current = Interlocked.Read(ref _maxTicks);
        while (ticks > current)
        {
            var seen = Interlocked.CompareExchange(ref _maxTicks, ticks, current);
            if (seen == current)
            {
                return;
            }

            current = seen;
        }
    }
}
