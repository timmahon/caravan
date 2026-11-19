namespace Caravan.Aggregation;

/// <summary>
/// How the <c>aggregate</c> EIP retries when optimistic locking detects a concurrent update (Camel's
/// <c>OptimisticLockRetryPolicy</c>). Delays are awaited on the context's <see cref="TimeProvider"/>.
/// </summary>
public sealed class OptimisticLockRetryPolicy
{
    /// <summary>The maximum number of attempts; 0 or less for no limit (default).</summary>
    public int MaximumRetries { get; set; }

    /// <summary>The delay before the first retry (default 50 ms); <see cref="TimeSpan.Zero"/> retries at once.</summary>
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromMilliseconds(50);

    /// <summary>The upper bound of the delay with <see cref="ExponentialBackOff"/> (default 1 s).</summary>
    public TimeSpan MaximumRetryDelay { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>Doubles the delay after each attempt, up to <see cref="MaximumRetryDelay"/> (default true).</summary>
    public bool ExponentialBackOff { get; set; } = true;

    /// <summary>Waits a random time between zero and the computed delay (default false).</summary>
    public bool RandomBackOff { get; set; }

    /// <summary>Whether another attempt is allowed after <paramref name="attempt"/> failed attempts.</summary>
    /// <param name="attempt">The number of attempts made (starting at 1).</param>
    public bool ShouldRetry(int attempt) => MaximumRetries <= 0 || attempt < MaximumRetries;

    /// <summary>The delay before the attempt following <paramref name="attempt"/>.</summary>
    /// <param name="attempt">The number of attempts made (starting at 1).</param>
    public TimeSpan GetDelay(int attempt)
    {
        var delay = RetryDelay;
        if (ExponentialBackOff && attempt > 1)
        {
            var factor = Math.Pow(2, Math.Min(attempt - 1, 30));
            delay = TimeSpan.FromTicks((long)Math.Min(RetryDelay.Ticks * factor, MaximumRetryDelay.Ticks));
        }

        if (delay > MaximumRetryDelay && ExponentialBackOff)
        {
            delay = MaximumRetryDelay;
        }

        return RandomBackOff ? TimeSpan.FromTicks((long)(Random.Shared.NextDouble() * delay.Ticks)) : delay;
    }

    /// <summary>Waits before the attempt following <paramref name="attempt"/>.</summary>
    /// <param name="attempt">The number of attempts made (starting at 1).</param>
    /// <param name="timeProvider">The clock.</param>
    /// <param name="cancellationToken">Cancels the wait.</param>
    public Task DelayAsync(int attempt, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        var delay = GetDelay(attempt);
        return delay > TimeSpan.Zero ? Task.Delay(delay, timeProvider, cancellationToken) : YieldAsync();

        static async Task YieldAsync() => await Task.Yield();
    }
}
