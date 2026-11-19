using Caravan.Model;

namespace Caravan.Builder;

/// <summary>Applies the options shared by the multicast-like DSL methods.</summary>
internal static class FanOutOptions
{
    public static void Apply(
        FanOutDefinition definition,
        IAggregationStrategy? aggregationStrategy,
        string? aggregationStrategyRef,
        bool parallelProcessing,
        bool parallelAggregate,
        int maxDegreeOfParallelism,
        bool streaming,
        bool stopOnException,
        TimeSpan? timeout,
        Action<Exchange>? onPrepare,
        bool shareUnitOfWork)
    {
        definition.AggregationStrategy = aggregationStrategy;
        definition.AggregationStrategyRef = aggregationStrategyRef;
        definition.ParallelProcessing = parallelProcessing;
        definition.ParallelAggregate = parallelAggregate;
        definition.MaxDegreeOfParallelism = maxDegreeOfParallelism;
        definition.Streaming = streaming;
        definition.StopOnException = stopOnException;
        definition.Timeout = timeout;
        definition.OnPrepare = onPrepare;
        definition.ShareUnitOfWork = shareUnitOfWork;
    }
}
