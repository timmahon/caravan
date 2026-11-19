namespace Caravan.Management;

/// <summary>
/// A snapshot of the performance counters of a context, route or node (Camel's JMX <c>ManagedPerformanceCounterMBean</c> attributes).
/// Processing times cover completed exchanges only (as in Camel); they are null until an exchange completed.
/// </summary>
public sealed record ProcessingStatistics
{
    /// <summary>Exchanges received (completed, failed and inflight).</summary>
    public long ExchangesTotal { get; init; }

    /// <summary>Exchanges completed successfully.</summary>
    public long ExchangesCompleted { get; init; }

    /// <summary>Exchanges that failed.</summary>
    public long ExchangesFailed { get; init; }

    /// <summary>Exchanges currently being processed.</summary>
    public long ExchangesInflight { get; init; }

    /// <summary>Failures handled by an error handler (dead letter channel or <c>onException</c> block).</summary>
    public long FailuresHandled { get; init; }

    /// <summary>Redelivery attempts made by error handlers.</summary>
    public long Redeliveries { get; init; }

    /// <summary>The shortest processing time of a completed exchange.</summary>
    public TimeSpan? MinProcessingTime { get; init; }

    /// <summary>The longest processing time of a completed exchange.</summary>
    public TimeSpan? MaxProcessingTime { get; init; }

    /// <summary>The mean processing time of completed exchanges.</summary>
    public TimeSpan? MeanProcessingTime { get; init; }

    /// <summary>The processing time of the last completed exchange.</summary>
    public TimeSpan? LastProcessingTime { get; init; }

    /// <summary>The sum of the processing times of completed exchanges.</summary>
    public TimeSpan TotalProcessingTime { get; init; }

    /// <summary>When the first exchange completed.</summary>
    public DateTimeOffset? FirstExchangeCompletedTimestamp { get; init; }

    /// <summary>When the last exchange completed.</summary>
    public DateTimeOffset? LastExchangeCompletedTimestamp { get; init; }

    /// <summary>When the last exchange failed.</summary>
    public DateTimeOffset? LastExchangeFailureTimestamp { get; init; }

    /// <summary>When the last exchange was received.</summary>
    public DateTimeOffset? LastExchangeCreatedTimestamp { get; init; }

    /// <summary>When the counters were last reset, if ever.</summary>
    public DateTimeOffset? ResetTimestamp { get; init; }
}
