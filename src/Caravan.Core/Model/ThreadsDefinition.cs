using Caravan.Processors;
using Caravan.Spi;

namespace Caravan.Model;

/// <summary>
/// The <c>threads</c> EIP (Camel's <c>threads</c>): an asynchronous boundary handing the exchange to a bounded worker pool for the rest of the
/// route (chained form, no <see cref="OutputDefinition.Outputs"/>: the steps following it in the same block) or for its block; see
/// <see cref="ThreadsProcessor"/>. The caller awaits the outcome, as a Camel caller does through its async callback, so the effect is the bounded
/// concurrency (a bulkhead) and the hand-off to another thread.
/// </summary>
public sealed class ThreadsDefinition : OutputDefinition
{
    /// <summary>The number of workers kept (Camel's core pool size, default 10).</summary>
    public int PoolSize { get; set; } = 10;

    /// <summary>The maximum number of workers, started when the queue is full (default 20).</summary>
    public int MaxPoolSize { get; set; } = 20;

    /// <summary>The number of exchanges waiting for a worker (default 1000; 0 for none; -1 for unbounded).</summary>
    public int MaxQueueSize { get; set; } = 1000;

    /// <summary>What happens when the pool and queue are full (default <see cref="ThreadPoolRejectedPolicy.CallerRuns"/>, Camel's default profile).</summary>
    public ThreadPoolRejectedPolicy RejectedPolicy { get; set; } = ThreadPoolRejectedPolicy.CallerRuns;

    /// <summary>A name for the pool (shown in logs and management).</summary>
    public string? ThreadName { get; set; }

    /// <summary>The steps following the definition in its block, when it has no block of its own (set while the route is created).</summary>
    internal IReadOnlyList<ProcessorDefinition>? RestOfBlock { get; set; }

    /// <inheritdoc />
    public override string ShortName => "threads";

    /// <inheritdoc />
    public override string Label => $"threads[{PoolSize}..{MaxPoolSize}, queue {MaxQueueSize}]";

    /// <inheritdoc />
    public override IProcessor CreateProcessor(RouteContext routeContext)
    {
        if (PoolSize < 1 || MaxPoolSize < PoolSize)
        {
            throw new ArgumentException($"threads needs 1 <= poolSize <= maxPoolSize, was {PoolSize} and {MaxPoolSize}");
        }

        var block = Outputs.Count > 0 ? routeContext.CreateBlockProcessor(Outputs, this) : routeContext.CreateBlockProcessor(RestOfBlock ?? []);
        return new ThreadsProcessor(block, PoolSize, MaxPoolSize, MaxQueueSize, RejectedPolicy, ThreadName ?? Id);
    }
}
