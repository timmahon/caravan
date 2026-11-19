using Caravan.Model;
using Microsoft.Extensions.Logging;

namespace Caravan.Builder;

/// <summary>
/// Redelivery options for error handlers (<c>DefaultErrorHandler()</c>, <c>DeadLetterChannel(uri)</c>) and <c>onException</c> clauses:
/// <c>DeadLetterChannel("mock:dead").MaximumRedeliveries(3).RedeliveryDelay(TimeSpan.FromSeconds(2)).UseExponentialBackOff()</c>.
/// See <see cref="Processors.RedeliveryPolicy"/> for the meaning and defaults of each option.
/// </summary>
public static class RedeliveryDsl
{
    /// <summary>The maximum number of redeliveries (0 = none, the default; -1 = forever).</summary>
    public static T MaximumRedeliveries<T>(this T target, int maximumRedeliveries)
        where T : IRedeliveryPolicyAware => Set(target, p => p.MaximumRedeliveries = maximumRedeliveries);

    /// <summary>The delay before the first redelivery (default 1 second).</summary>
    public static T RedeliveryDelay<T>(this T target, TimeSpan delay)
        where T : IRedeliveryPolicyAware => Set(target, p => p.RedeliveryDelay = delay);

    /// <summary>The delay before the first redelivery, in milliseconds (default 1000).</summary>
    public static T RedeliveryDelay<T>(this T target, long milliseconds)
        where T : IRedeliveryPolicyAware => target.RedeliveryDelay(TimeSpan.FromMilliseconds(milliseconds));

    /// <summary>The upper bound of the delay between redeliveries (default 60 seconds).</summary>
    public static T MaximumRedeliveryDelay<T>(this T target, TimeSpan delay)
        where T : IRedeliveryPolicyAware => Set(target, p => p.MaximumRedeliveryDelay = delay);

    /// <summary>The upper bound of the delay between redeliveries, in milliseconds (default 60000).</summary>
    public static T MaximumRedeliveryDelay<T>(this T target, long milliseconds)
        where T : IRedeliveryPolicyAware => target.MaximumRedeliveryDelay(TimeSpan.FromMilliseconds(milliseconds));

    /// <summary>Multiplies each delay by the back-off multiplier (default 2).</summary>
    public static T UseExponentialBackOff<T>(this T target, bool enabled = true)
        where T : IRedeliveryPolicyAware => Set(target, p => p.UseExponentialBackOff = enabled);

    /// <summary>The multiplier used with exponential back-off.</summary>
    public static T BackOffMultiplier<T>(this T target, double multiplier)
        where T : IRedeliveryPolicyAware => Set(target, p => p.BackOffMultiplier = multiplier);

    /// <summary>Adds a random variation to each delay (see <see cref="CollisionAvoidanceFactor{T}"/>).</summary>
    public static T UseCollisionAvoidance<T>(this T target, bool enabled = true)
        where T : IRedeliveryPolicyAware => Set(target, p => p.UseCollisionAvoidance = enabled);

    /// <summary>The maximum relative variation of the delay with collision avoidance (default 0.15).</summary>
    public static T CollisionAvoidanceFactor<T>(this T target, double factor)
        where T : IRedeliveryPolicyAware => Set(target, p => p.CollisionAvoidanceFactor = factor);

    /// <summary>Delays by attempt, e.g. <c>"0:1000;5:5000"</c> (overrides the other delay options).</summary>
    /// <exception cref="ArgumentException">The pattern is malformed.</exception>
    public static T DelayPattern<T>(this T target, string pattern)
        where T : IRedeliveryPolicyAware
    {
        Processors.RedeliveryPolicy.ValidateDelayPattern(pattern);
        return Set(target, p => p.DelayPattern = pattern);
    }

    /// <summary>Kept for compatibility with Camel: delays are always non-blocking in Caravan.</summary>
    public static T AsyncDelayedRedelivery<T>(this T target, bool enabled = true)
        where T : IRedeliveryPolicyAware => Set(target, p => p.AsyncDelayedRedelivery = enabled);

    /// <summary>Whether failed attempts that will be redelivered are logged (default true).</summary>
    public static T LogRetryAttempted<T>(this T target, bool enabled = true)
        where T : IRedeliveryPolicyAware => Set(target, p => p.LogRetryAttempted = enabled);

    /// <summary>The level used to log failed attempts that will be redelivered (default Debug).</summary>
    public static T RetryAttemptedLogLevel<T>(this T target, LogLevel level)
        where T : IRedeliveryPolicyAware => Set(target, p => p.RetryAttemptedLogLevel = level);

    /// <summary>The level used to log exhausted failures (default Error).</summary>
    public static T RetriesExhaustedLogLevel<T>(this T target, LogLevel level)
        where T : IRedeliveryPolicyAware => Set(target, p => p.RetriesExhaustedLogLevel = level);

    /// <summary>Whether exhausted failures are logged (default true).</summary>
    public static T LogExhausted<T>(this T target, bool enabled = true)
        where T : IRedeliveryPolicyAware => Set(target, p => p.LogExhausted = enabled);

    /// <summary>Whether failures handled by an <c>onException</c> clause are logged (default false).</summary>
    public static T LogHandled<T>(this T target, bool enabled = true)
        where T : IRedeliveryPolicyAware => Set(target, p => p.LogHandled = enabled);

    /// <summary>Whether failures continued by an <c>onException</c> clause are logged (default false).</summary>
    public static T LogContinued<T>(this T target, bool enabled = true)
        where T : IRedeliveryPolicyAware => Set(target, p => p.LogContinued = enabled);

    /// <summary>Whether the stack trace is logged with exhausted failures (default true).</summary>
    public static T LogStackTrace<T>(this T target, bool enabled = true)
        where T : IRedeliveryPolicyAware => Set(target, p => p.LogStackTrace = enabled);

    /// <summary>Whether the stack trace is logged with failed attempts that will be redelivered (default false).</summary>
    public static T LogRetryStackTrace<T>(this T target, bool enabled = true)
        where T : IRedeliveryPolicyAware => Set(target, p => p.LogRetryStackTrace = enabled);

    /// <summary>Whether a new exception thrown while handling a failure is logged (default true).</summary>
    public static T LogNewException<T>(this T target, bool enabled = true)
        where T : IRedeliveryPolicyAware => Set(target, p => p.LogNewException = enabled);

    /// <summary>Whether the exchange (id, headers, body) is included when logging an exhausted failure (default true).</summary>
    public static T LogExhaustedExchange<T>(this T target, bool enabled = true)
        where T : IRedeliveryPolicyAware => Set(target, p => p.LogExhaustedExchange = enabled);

    /// <summary>
    /// Whether the message history table is included when logging an exhausted failure (default true; requires
    /// <see cref="ICaravanContext.MessageHistory"/>).
    /// </summary>
    public static T LogExhaustedMessageHistory<T>(this T target, bool enabled = true)
        where T : IRedeliveryPolicyAware => Set(target, p => p.LogExhaustedMessageHistory = enabled);

    private static T Set<T>(T target, Action<RedeliveryPolicyDefinition> set)
        where T : IRedeliveryPolicyAware
    {
        set(target.RedeliveryPolicy);
        return target;
    }
}
