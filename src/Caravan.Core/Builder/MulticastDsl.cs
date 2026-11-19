using Caravan.Model;

namespace Caravan.Builder;

/// <summary>DSL for the <c>multicast</c> EIP.</summary>
public static class MulticastDsl
{
    /// <summary>
    /// Sends a copy of the exchange to each step of <paramref name="branches"/> (each step is a branch; use <c>Pipeline</c> to group steps)
    /// and aggregates the replies into the exchange: <c>.Multicast(m =&gt; m.To("direct:a").To("direct:b"), parallelProcessing: true)</c>.
    /// Branch properties: <see cref="ExchangePropertyKeys.MulticastIndex"/>, <see cref="ExchangePropertyKeys.MulticastComplete"/>.
    /// </summary>
    /// <param name="steps">The block to add to.</param>
    /// <param name="branches">The branches.</param>
    /// <param name="aggregationStrategy">Combines the branch results; null for the latest reply (<see cref="AggregationStrategies.UseLatest"/>).</param>
    /// <param name="aggregationStrategyRef">The name of an <see cref="IAggregationStrategy"/> in the registry, used when <paramref name="aggregationStrategy"/> is null.</param>
    /// <param name="parallelProcessing">Process the branches concurrently on the thread pool (at most <paramref name="maxDegreeOfParallelism"/> at a time).</param>
    /// <param name="parallelAggregate">With parallel processing and streaming, call the strategy concurrently (it must be thread-safe).</param>
    /// <param name="maxDegreeOfParallelism">The maximum number of branches processed concurrently; 0 for the default (10).</param>
    /// <param name="streaming">With parallel processing, aggregate the replies in the order they complete instead of the branch order.</param>
    /// <param name="stopOnException">Stop at the first failed branch: no further branch is processed and the exchange fails with a <see cref="CaravanExchangeException"/> wrapping the failure.</param>
    /// <param name="timeout">With parallel processing, the total time to wait; unfinished branches are then cancelled and ignored, and the strategy's <see cref="IAggregationStrategy.OnTimeout"/> is called for each.</param>
    /// <param name="onPrepare">Called on each branch exchange before it is processed (e.g. to deep-copy a mutable body).</param>
    /// <param name="shareUnitOfWork">Branches share the exchange's unit of work: their completion callbacks run when the exchange completes, and a failure is handled by the exchange's error handler (after the branch's own redeliveries).</param>
    public static T Multicast<T>(
        this T steps,
        Action<RouteSteps> branches,
        IAggregationStrategy? aggregationStrategy = null,
        string? aggregationStrategyRef = null,
        bool parallelProcessing = false,
        bool parallelAggregate = false,
        int maxDegreeOfParallelism = 0,
        bool streaming = false,
        bool stopOnException = false,
        TimeSpan? timeout = null,
        Action<Exchange>? onPrepare = null,
        bool shareUnitOfWork = false)
        where T : RouteSteps
    {
        var definition = steps.AddStep(new MulticastDefinition());
        FanOutOptions.Apply(definition, aggregationStrategy, aggregationStrategyRef, parallelProcessing, parallelAggregate, maxDegreeOfParallelism, streaming, stopOnException, timeout, onPrepare, shareUnitOfWork);
        steps.BuildNested(definition.Outputs, definition, branches);
        return steps;
    }
}
