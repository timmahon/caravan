namespace Caravan.Processors;

/// <summary>Options of the multicast engine shared by <c>multicast</c>, <c>split</c> and <c>recipientList</c> (see <see cref="MulticastProcessor"/>).</summary>
public sealed class MulticastSettings
{
    /// <summary>The maximum number of branches processed concurrently when none is given (Camel's default thread pool size).</summary>
    public const int DefaultMaxDegreeOfParallelism = 10;

    /// <summary>Combines the branch results; the result is copied into the input exchange when all branches are done.</summary>
    public required IAggregationStrategy AggregationStrategy { get; init; }

    /// <summary>Process branches concurrently (up to <see cref="MaxDegreeOfParallelism"/>) on the thread pool.</summary>
    public bool ParallelProcessing { get; init; }

    /// <summary>
    /// With <see cref="ParallelProcessing"/> and <see cref="Streaming"/>: aggregate on the branch tasks without serializing the
    /// calls, so the strategy must be thread-safe. In ordered (non-streaming) mode aggregation is always serialized.
    /// </summary>
    public bool ParallelAggregate { get; init; }

    /// <summary>The maximum number of branches processed concurrently with <see cref="ParallelProcessing"/>.</summary>
    public int MaxDegreeOfParallelism { get; init; } = DefaultMaxDegreeOfParallelism;

    /// <summary>
    /// With <see cref="ParallelProcessing"/>, aggregate replies in the order they complete instead of the branch order. The splitter
    /// also splits lazily in streaming mode (see <see cref="SplitProcessor"/>).
    /// </summary>
    public bool Streaming { get; init; }

    /// <summary>
    /// Stop processing further branches when one fails (exception, rollback, or failure handled by an error handler). The input
    /// exchange then fails with a <see cref="CaravanExchangeException"/> wrapping the exception (or gets the result of the branch
    /// whose failure an error handler handled).
    /// </summary>
    public bool StopOnException { get; init; }

    /// <summary>
    /// With <see cref="ParallelProcessing"/>: the total time to wait for the branches. On timeout, the strategy's
    /// <see cref="IAggregationStrategy.OnTimeout"/> is called for every unfinished branch, the unfinished branches are cancelled
    /// (through <see cref="Exchange.CancellationToken"/>) and ignored, and routing continues with what was aggregated.
    /// </summary>
    public TimeSpan? Timeout { get; init; }

    /// <summary>Called on each branch exchange before it is processed (e.g. to deep-copy a mutable body).</summary>
    public Action<Exchange>? OnPrepare { get; init; }

    /// <summary>
    /// Branch exchanges share the input exchange's unit of work instead of getting their own: their completion callbacks run when the
    /// input exchange completes, and a branch failure is handled by the input exchange's error handler (after the branch's own
    /// redeliveries) instead of the branch's.
    /// </summary>
    public bool ShareUnitOfWork { get; init; }
}
