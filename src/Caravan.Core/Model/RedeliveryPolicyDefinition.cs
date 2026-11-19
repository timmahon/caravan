using Caravan.Processors;
using Microsoft.Extensions.Logging;

namespace Caravan.Model;

/// <summary>
/// Redelivery options of an error handler or an <c>onException</c> clause. Only the options that are set are applied:
/// an <c>onException</c> clause starts from its error handler's policy and overrides what it sets (as in Camel).
/// </summary>
public sealed class RedeliveryPolicyDefinition
{
    /// <summary>See <see cref="RedeliveryPolicy.MaximumRedeliveries"/>.</summary>
    public int? MaximumRedeliveries { get; set; }

    /// <summary>See <see cref="RedeliveryPolicy.RedeliveryDelay"/>.</summary>
    public TimeSpan? RedeliveryDelay { get; set; }

    /// <summary>See <see cref="RedeliveryPolicy.MaximumRedeliveryDelay"/>.</summary>
    public TimeSpan? MaximumRedeliveryDelay { get; set; }

    /// <summary>See <see cref="RedeliveryPolicy.UseExponentialBackOff"/>.</summary>
    public bool? UseExponentialBackOff { get; set; }

    /// <summary>See <see cref="RedeliveryPolicy.BackOffMultiplier"/>.</summary>
    public double? BackOffMultiplier { get; set; }

    /// <summary>See <see cref="RedeliveryPolicy.UseCollisionAvoidance"/>.</summary>
    public bool? UseCollisionAvoidance { get; set; }

    /// <summary>See <see cref="RedeliveryPolicy.CollisionAvoidanceFactor"/>.</summary>
    public double? CollisionAvoidanceFactor { get; set; }

    /// <summary>See <see cref="RedeliveryPolicy.DelayPattern"/>.</summary>
    public string? DelayPattern { get; set; }

    /// <summary>See <see cref="RedeliveryPolicy.AsyncDelayedRedelivery"/>.</summary>
    public bool? AsyncDelayedRedelivery { get; set; }

    /// <summary>See <see cref="RedeliveryPolicy.LogRetryAttempted"/>.</summary>
    public bool? LogRetryAttempted { get; set; }

    /// <summary>See <see cref="RedeliveryPolicy.RetryAttemptedLogLevel"/>.</summary>
    public LogLevel? RetryAttemptedLogLevel { get; set; }

    /// <summary>See <see cref="RedeliveryPolicy.RetriesExhaustedLogLevel"/>.</summary>
    public LogLevel? RetriesExhaustedLogLevel { get; set; }

    /// <summary>See <see cref="RedeliveryPolicy.LogExhausted"/>.</summary>
    public bool? LogExhausted { get; set; }

    /// <summary>See <see cref="RedeliveryPolicy.LogHandled"/>.</summary>
    public bool? LogHandled { get; set; }

    /// <summary>See <see cref="RedeliveryPolicy.LogContinued"/>.</summary>
    public bool? LogContinued { get; set; }

    /// <summary>See <see cref="RedeliveryPolicy.LogStackTrace"/>.</summary>
    public bool? LogStackTrace { get; set; }

    /// <summary>See <see cref="RedeliveryPolicy.LogRetryStackTrace"/>.</summary>
    public bool? LogRetryStackTrace { get; set; }

    /// <summary>See <see cref="RedeliveryPolicy.LogNewException"/>.</summary>
    public bool? LogNewException { get; set; }

    /// <summary>See <see cref="RedeliveryPolicy.LogExhaustedExchange"/>.</summary>
    public bool? LogExhaustedExchange { get; set; }

    /// <summary>See <see cref="RedeliveryPolicy.LogExhaustedMessageHistory"/>.</summary>
    public bool? LogExhaustedMessageHistory { get; set; }

    /// <summary>Whether these options allow redelivery (a maximum other than 0 is set).</summary>
    public bool EnablesRedelivery => MaximumRedeliveries is { } max && max != 0;

    /// <summary>Creates a policy: a copy of <paramref name="parent"/> (or the defaults) with the options set here applied.</summary>
    public RedeliveryPolicy CreatePolicy(RedeliveryPolicy? parent = null)
    {
        var policy = parent?.Copy() ?? new RedeliveryPolicy();
        policy.MaximumRedeliveries = MaximumRedeliveries ?? policy.MaximumRedeliveries;
        policy.RedeliveryDelay = RedeliveryDelay ?? policy.RedeliveryDelay;
        policy.MaximumRedeliveryDelay = MaximumRedeliveryDelay ?? policy.MaximumRedeliveryDelay;
        policy.UseExponentialBackOff = UseExponentialBackOff ?? policy.UseExponentialBackOff;
        policy.BackOffMultiplier = BackOffMultiplier ?? policy.BackOffMultiplier;
        policy.UseCollisionAvoidance = UseCollisionAvoidance ?? policy.UseCollisionAvoidance;
        policy.CollisionAvoidanceFactor = CollisionAvoidanceFactor ?? policy.CollisionAvoidanceFactor;
        policy.DelayPattern = DelayPattern ?? policy.DelayPattern;
        policy.AsyncDelayedRedelivery = AsyncDelayedRedelivery ?? policy.AsyncDelayedRedelivery;
        policy.LogRetryAttempted = LogRetryAttempted ?? policy.LogRetryAttempted;
        policy.RetryAttemptedLogLevel = RetryAttemptedLogLevel ?? policy.RetryAttemptedLogLevel;
        policy.RetriesExhaustedLogLevel = RetriesExhaustedLogLevel ?? policy.RetriesExhaustedLogLevel;
        policy.LogExhausted = LogExhausted ?? policy.LogExhausted;
        policy.LogHandled = LogHandled ?? policy.LogHandled;
        policy.LogContinued = LogContinued ?? policy.LogContinued;
        policy.LogStackTrace = LogStackTrace ?? policy.LogStackTrace;
        policy.LogRetryStackTrace = LogRetryStackTrace ?? policy.LogRetryStackTrace;
        policy.LogNewException = LogNewException ?? policy.LogNewException;
        policy.LogExhaustedExchange = LogExhaustedExchange ?? policy.LogExhaustedExchange;
        policy.LogExhaustedMessageHistory = LogExhaustedMessageHistory ?? policy.LogExhaustedMessageHistory;
        return policy;
    }
}
