using System.Globalization;
using Microsoft.Extensions.Logging;

namespace Caravan.Processors;

/// <summary>
/// How an error handler redelivers a failed exchange and what it logs (Camel's <c>RedeliveryPolicy</c>).
/// Defaults match Camel: no redelivery, 1 second delay, back-off multiplier 2, maximum delay 60 seconds.
/// </summary>
public sealed class RedeliveryPolicy
{
    /// <summary>
    /// The maximum number of redeliveries after the first attempt: 0 (default) disables redelivery, -1 redelivers forever.
    /// Ignored when a <c>retryWhile</c> predicate is configured.
    /// </summary>
    public int MaximumRedeliveries { get; set; }

    /// <summary>The delay before the first redelivery (default 1 second).</summary>
    public TimeSpan RedeliveryDelay { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>The upper bound of the delay between redeliveries (default 60 seconds).</summary>
    public TimeSpan MaximumRedeliveryDelay { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>Whether each delay is the previous one multiplied by <see cref="BackOffMultiplier"/>.</summary>
    public bool UseExponentialBackOff { get; set; }

    /// <summary>The multiplier used with <see cref="UseExponentialBackOff"/> (default 2).</summary>
    public double BackOffMultiplier { get; set; } = 2;

    /// <summary>Whether to add a random variation of up to ±<see cref="CollisionAvoidanceFactor"/> to each delay.</summary>
    public bool UseCollisionAvoidance { get; set; }

    /// <summary>The maximum relative variation used with <see cref="UseCollisionAvoidance"/> (default 0.15).</summary>
    public double CollisionAvoidanceFactor { get; set; } = 0.15;

    /// <summary>
    /// Delays by attempt, overriding the other delay options: <c>"0:1000;5:5000"</c> waits 1000 ms before redeliveries 1 to 4
    /// and 5000 ms from redelivery 5 on (each group is <c>attempt:milliseconds</c>, in increasing attempt order).
    /// </summary>
    public string? DelayPattern { get; set; }

    /// <summary>
    /// Whether the delay before a redelivery is non-blocking (Camel's <c>asyncDelayedRedelivery</c>). Delays in Caravan are always
    /// awaited asynchronously on the context's <see cref="TimeProvider"/>, so this option is kept for compatibility only.
    /// </summary>
    public bool AsyncDelayedRedelivery { get; set; }

    /// <summary>Whether failed attempts that will be redelivered are logged (default true, at <see cref="RetryAttemptedLogLevel"/>).</summary>
    public bool LogRetryAttempted { get; set; } = true;

    /// <summary>The level used to log failed attempts that will be redelivered (default <see cref="LogLevel.Debug"/>).</summary>
    public LogLevel RetryAttemptedLogLevel { get; set; } = LogLevel.Debug;

    /// <summary>The level used to log the failure once redelivery is exhausted (default <see cref="LogLevel.Error"/>).</summary>
    public LogLevel RetriesExhaustedLogLevel { get; set; } = LogLevel.Error;

    /// <summary>Whether exhausted failures are logged (default true).</summary>
    public bool LogExhausted { get; set; } = true;

    /// <summary>Whether failures handled by an <c>onException</c> block are logged (default false).</summary>
    public bool LogHandled { get; set; }

    /// <summary>Whether failures continued by an <c>onException</c> block are logged (default false).</summary>
    public bool LogContinued { get; set; }

    /// <summary>Whether the stack trace is logged with exhausted failures (default true).</summary>
    public bool LogStackTrace { get; set; } = true;

    /// <summary>Whether the stack trace is logged with failed attempts that will be redelivered (default false).</summary>
    public bool LogRetryStackTrace { get; set; }

    /// <summary>Whether a new exception thrown while handling a failure (by a failure processor) is logged (default true).</summary>
    public bool LogNewException { get; set; } = true;

    /// <summary>Whether the exchange (body, headers, ...) is included when logging an exhausted failure (default true).</summary>
    public bool LogExhaustedExchange { get; set; } = true;

    /// <summary>Creates a copy of this policy.</summary>
    public RedeliveryPolicy Copy() => (RedeliveryPolicy)MemberwiseClone();

    /// <summary>
    /// Whether another redelivery should be attempted after the failure number <paramref name="redeliveryCounter"/>
    /// (1 after the first failure): <paramref name="retryWhile"/> decides when given, otherwise <see cref="MaximumRedeliveries"/>.
    /// </summary>
    public bool ShouldRedeliver(Exchange exchange, int redeliveryCounter, IPredicate? retryWhile)
    {
        if (retryWhile is not null)
        {
            return retryWhile.Matches(exchange);
        }

        if (MaximumRedeliveries < 0)
        {
            return true;
        }

        return redeliveryCounter <= MaximumRedeliveries;
    }

    /// <summary>
    /// Calculates the delay before the redelivery number <paramref name="redeliveryCounter"/> (starting at 1), given the
    /// <paramref name="previousDelay"/> (zero before the first redelivery). Same algorithm as Camel.
    /// </summary>
    public TimeSpan CalculateRedeliveryDelay(TimeSpan previousDelay, int redeliveryCounter)
    {
        if (DelayPattern is { Length: > 0 } pattern)
        {
            return CalculateDelayFromPattern(pattern, redeliveryCounter);
        }

        double delay;
        if (previousDelay <= TimeSpan.Zero)
        {
            delay = RedeliveryDelay.TotalMilliseconds;
        }
        else if (UseExponentialBackOff && BackOffMultiplier > 1)
        {
            delay = Math.Round(BackOffMultiplier * previousDelay.TotalMilliseconds);
        }
        else
        {
            delay = previousDelay.TotalMilliseconds;
        }

        if (UseCollisionAvoidance)
        {
            // The first random value picks the direction, the second how far to go.
            var variance = (Random.Shared.Next(2) == 0 ? CollisionAvoidanceFactor : -CollisionAvoidanceFactor) * Random.Shared.NextDouble();
            delay += delay * variance;
        }

        var max = MaximumRedeliveryDelay.TotalMilliseconds;
        if (max > 0 && delay > max)
        {
            delay = max;
        }

        return TimeSpan.FromMilliseconds(Math.Max(0, delay));
    }

    /// <inheritdoc />
    public override string ToString() =>
        $"RedeliveryPolicy[maximumRedeliveries={MaximumRedeliveries}, redeliveryDelay={RedeliveryDelay.TotalMilliseconds}ms, " +
        $"maximumRedeliveryDelay={MaximumRedeliveryDelay.TotalMilliseconds}ms, useExponentialBackOff={UseExponentialBackOff}, " +
        $"backOffMultiplier={BackOffMultiplier}, useCollisionAvoidance={UseCollisionAvoidance}, delayPattern={DelayPattern}]";

    /// <summary>Validates a delay pattern such as <c>"0:1000;5:5000"</c>.</summary>
    /// <exception cref="ArgumentException">The pattern is malformed.</exception>
    public static void ValidateDelayPattern(string pattern)
    {
        foreach (var group in pattern.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = group.Split(':');
            if (parts.Length != 2 ||
                !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out _) ||
                !long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
            {
                throw new ArgumentException($"Invalid delay pattern group '{group}' in '{pattern}': expected attempt:milliseconds", nameof(pattern));
            }
        }
    }

    private static TimeSpan CalculateDelayFromPattern(string pattern, int redeliveryCounter)
    {
        long answer = 0;
        foreach (var group in pattern.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = group.Split(':');
            var count = int.Parse(parts[0], CultureInfo.InvariantCulture);
            if (count > redeliveryCounter)
            {
                break;
            }

            answer = long.Parse(parts[1], CultureInfo.InvariantCulture);
        }

        return TimeSpan.FromMilliseconds(answer);
    }
}
