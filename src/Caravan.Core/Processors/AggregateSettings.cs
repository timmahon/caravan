using Caravan.Aggregation;
using Caravan.Spi;

namespace Caravan.Processors;

/// <summary>Options of the <c>aggregate</c> EIP (see <see cref="AggregateProcessor"/> and <see cref="Model.AggregateDefinition"/>).</summary>
public sealed class AggregateSettings
{
    /// <summary>The default <see cref="CompletionTimeoutCheckerInterval"/> (1 s, as in Camel).</summary>
    public static readonly TimeSpan DefaultCompletionTimeoutCheckerInterval = TimeSpan.FromSeconds(1);

    /// <summary>The default <see cref="MaxDegreeOfParallelism"/> with parallel processing (Camel's default thread pool size).</summary>
    public const int DefaultMaxDegreeOfParallelism = 10;

    /// <summary>Completes a group once it holds this many exchanges (0 to disable).</summary>
    public int CompletionSize { get; init; }

    /// <summary>
    /// Computes the completion size per exchange (an integer); takes precedence over <see cref="CompletionSize"/> when it yields a
    /// positive value.
    /// </summary>
    public IExpression? CompletionSizeExpression { get; init; }

    /// <summary>
    /// Completes a group that received no exchange for this long (inactivity: each exchange restarts the timeout). Checked every
    /// <see cref="CompletionTimeoutCheckerInterval"/>. Cannot be combined with <see cref="CompletionInterval"/>.
    /// </summary>
    public TimeSpan? CompletionTimeout { get; init; }

    /// <summary>
    /// Computes the completion timeout per exchange (a <see cref="TimeSpan"/> or a number of milliseconds); takes precedence over
    /// <see cref="CompletionTimeout"/> when it yields a positive value.
    /// </summary>
    public IExpression? CompletionTimeoutExpression { get; init; }

    /// <summary>How often timeouts are checked (default 1 s).</summary>
    public TimeSpan CompletionTimeoutCheckerInterval { get; init; } = DefaultCompletionTimeoutCheckerInterval;

    /// <summary>Completes all groups periodically, every interval from the start of the aggregator.</summary>
    public TimeSpan? CompletionInterval { get; init; }

    /// <summary>Completes a group when the predicate matches the aggregated exchange (the new exchange with <see cref="EagerCheckCompletion"/>).</summary>
    public IPredicate? CompletionPredicate { get; init; }

    /// <summary>
    /// Completes all groups touched by a batch when the batch consumer's batch is done: when as many exchanges as
    /// <see cref="ExchangePropertyKeys.BatchSize"/> arrived, or one with <see cref="ExchangePropertyKeys.BatchComplete"/> set.
    /// </summary>
    public bool CompletionFromBatchConsumer { get; init; }

    /// <summary>Completes all other groups when an exchange starts a new group.</summary>
    public bool CompletionOnNewCorrelationGroup { get; init; }

    /// <summary>
    /// Checks completion on the incoming exchange before aggregating it (with <see cref="ExchangePropertyKeys.AggregatedSize"/> set to
    /// what the group's size will be), instead of on the aggregated exchange.
    /// </summary>
    public bool EagerCheckCompletion { get; init; }

    /// <summary>Completes all groups when the route stops (not when it is suspended or the stop is forced).</summary>
    public bool ForceCompletionOnStop { get; init; }

    /// <summary>
    /// Counts the stored groups as pending exchanges when the route stops, so a graceful stop waits until they completed (by timeout
    /// or interval) before stopping the aggregator.
    /// </summary>
    public bool CompleteAllOnStop { get; init; }

    /// <summary>Ignores exchanges whose correlation key is null or blank instead of failing them.</summary>
    public bool IgnoreInvalidCorrelationKeys { get; init; }

    /// <summary>
    /// Closes the correlation key of a completed group, so later exchanges for it fail with a <see cref="ClosedCorrelationKeyException"/>;
    /// the value is the number of keys remembered (the oldest are forgotten first), 0 for no limit. Null to disable.
    /// </summary>
    public int? CloseCorrelationKeyOnCompletion { get; init; }

    /// <summary>Discards groups completed by timeout instead of sending them (the strategy's <see cref="IAggregationStrategy.OnTimeout"/> is still called).</summary>
    public bool DiscardOnCompletionTimeout { get; init; }

    /// <summary>
    /// When the strategy throws, discards the group (and the incoming exchange) instead of failing the incoming exchange.
    /// </summary>
    public bool DiscardOnAggregationFailure { get; init; }

    /// <summary>Sends completed groups on the thread pool, at most <see cref="MaxDegreeOfParallelism"/> at a time.</summary>
    public bool ParallelProcessing { get; init; }

    /// <summary>The maximum number of completed groups sent concurrently with <see cref="ParallelProcessing"/>.</summary>
    public int MaxDegreeOfParallelism { get; init; } = DefaultMaxDegreeOfParallelism;

    /// <summary>
    /// Aggregates without a lock, relying on the repository (an <see cref="IOptimisticLockingAggregationRepository"/>) to detect
    /// concurrent updates; failed attempts are retried with <see cref="OptimisticLockRetryPolicy"/>.
    /// </summary>
    public bool OptimisticLocking { get; init; }

    /// <summary>How optimistic locking failures are retried.</summary>
    public OptimisticLockRetryPolicy OptimisticLockRetryPolicy { get; init; } = new();

    /// <summary>Stores the groups; default a new <see cref="MemoryAggregationRepository"/>.</summary>
    public IAggregationRepository? AggregationRepository { get; init; }

    /// <summary>Lets application code force the completion or discarding of groups.</summary>
    public IAggregateController? AggregateController { get; init; }
}
