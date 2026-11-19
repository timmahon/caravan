namespace Caravan.Model;

/// <summary>
/// The options of a <c>circuitBreaker</c> EIP (Camel's <c>resilience4jConfiguration</c>), mapped to a Polly v8 circuit breaker, timeout and a
/// bulkhead. Every option is optional: an EIP's effective configuration is built from the defaults below, overridden by the context's default
/// configuration (<see cref="CircuitBreakerContextExtensions.SetDefaultCircuitBreakerConfiguration"/>), then by the referenced named
/// configuration (<see cref="CircuitBreakerDefinition.ConfigurationRef"/>), then by the EIP's own options.
/// </summary>
/// <remarks>
/// Polly's breaker is time-based: it opens when, within <see cref="SamplingDuration"/>, at least <see cref="MinimumThroughput"/> calls were
/// made and the ratio of failed ones reached <see cref="FailureRatio"/> (Resilience4j's default is a count-based window of 100 calls). After
/// <see cref="BreakDuration"/> one call probes the circuit (half-open): success closes it, failure opens it again (Resilience4j lets
/// <c>permittedNumberOfCallsInHalfOpenState</c> calls through; Polly one).
/// </remarks>
public sealed class CircuitBreakerConfiguration
{
    /// <summary>The default <see cref="FailureRatio"/>: 0.5 (Resilience4j's <c>failureRateThreshold</c> of 50%).</summary>
    public const double DefaultFailureRatio = 0.5;

    /// <summary>The default <see cref="MinimumThroughput"/>: 100 (Resilience4j's <c>minimumNumberOfCalls</c>).</summary>
    public const int DefaultMinimumThroughput = 100;

    /// <summary>The default <see cref="SamplingDuration"/>: 30 seconds (Polly's default).</summary>
    public static readonly TimeSpan DefaultSamplingDuration = TimeSpan.FromSeconds(30);

    /// <summary>The default <see cref="BreakDuration"/>: 60 seconds (Resilience4j's <c>waitDurationInOpenState</c>).</summary>
    public static readonly TimeSpan DefaultBreakDuration = TimeSpan.FromSeconds(60);

    /// <summary>The ratio of failed calls (0 to 1] at which the circuit opens.</summary>
    public double? FailureRatio { get; set; }

    /// <summary>The minimum number of calls within <see cref="SamplingDuration"/> before the circuit can open (at least 2).</summary>
    public int? MinimumThroughput { get; set; }

    /// <summary>The window over which failures are counted (at least 500 ms).</summary>
    public TimeSpan? SamplingDuration { get; set; }

    /// <summary>How long the circuit stays open before a probe call is let through (at least 500 ms).</summary>
    public TimeSpan? BreakDuration { get; set; }

    /// <summary>
    /// The maximum time a call may take (Resilience4j's <c>timeoutDuration</c> with <c>timeoutEnabled</c>); a positive value enables the time
    /// limit, <see cref="TimeSpan.Zero"/> disables an inherited one. A timed out call counts as a failure and is abandoned: the block's work goes
    /// on (on a copy of the exchange) but its result is ignored.
    /// </summary>
    public TimeSpan? Timeout { get; set; }

    /// <summary>
    /// The maximum number of calls running at a time (Resilience4j's bulkhead <c>maxConcurrentCalls</c>); a positive value enables the bulkhead,
    /// zero disables an inherited one.
    /// </summary>
    public int? BulkheadMaxConcurrentCalls { get; set; }

    /// <summary>
    /// How many calls may wait for a free slot when <see cref="BulkheadMaxConcurrentCalls"/> calls are running (default 0); calls beyond it are
    /// rejected with a <see cref="BulkheadRejectedException"/>.
    /// </summary>
    public int? BulkheadMaxWaitingCalls { get; set; }

    /// <summary>
    /// Exceptions that count as failures (matched along the cause chain, subtypes included); when set, all others count as successes for the
    /// circuit (Resilience4j's <c>recordExceptions</c>). The fallback still runs for any failure.
    /// </summary>
    public IList<Type>? RecordExceptions { get; set; }

    /// <summary>
    /// Exceptions that do not count as failures for the circuit (Resilience4j's <c>ignoreExceptions</c>). The fallback still runs for them.
    /// </summary>
    public IList<Type>? IgnoreExceptions { get; set; }

    /// <summary>A copy of this configuration with the options set on <paramref name="other"/> taking precedence.</summary>
    /// <param name="other">The overriding configuration; null returns a copy of this one.</param>
    /// <returns>The merged configuration.</returns>
    public CircuitBreakerConfiguration OverrideWith(CircuitBreakerConfiguration? other) => new()
    {
        FailureRatio = other?.FailureRatio ?? FailureRatio,
        MinimumThroughput = other?.MinimumThroughput ?? MinimumThroughput,
        SamplingDuration = other?.SamplingDuration ?? SamplingDuration,
        BreakDuration = other?.BreakDuration ?? BreakDuration,
        Timeout = other?.Timeout ?? Timeout,
        BulkheadMaxConcurrentCalls = other?.BulkheadMaxConcurrentCalls ?? BulkheadMaxConcurrentCalls,
        BulkheadMaxWaitingCalls = other?.BulkheadMaxWaitingCalls ?? BulkheadMaxWaitingCalls,
        RecordExceptions = other?.RecordExceptions is { Count: > 0 } record ? [.. record] : RecordExceptions is null ? null : [.. RecordExceptions],
        IgnoreExceptions = other?.IgnoreExceptions is { Count: > 0 } ignore ? [.. ignore] : IgnoreExceptions is null ? null : [.. IgnoreExceptions],
    };
}
