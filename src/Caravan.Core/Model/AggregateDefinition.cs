using Caravan.Support;
using Caravan.Aggregation;
using Caravan.Processors;
using Caravan.Spi;

namespace Caravan.Model;

/// <summary>
/// The <c>aggregate</c> EIP: exchanges are grouped by <see cref="CorrelationExpression"/> and combined with the aggregation strategy;
/// each completed group is sent through <see cref="OutputDefinition.Outputs"/> as a new exchange, while the incoming exchanges continue
/// unchanged. At least one completion condition is required. See <see cref="AggregateProcessor"/> and <see cref="AggregateSettings"/>
/// for the options.
/// </summary>
/// <remarks>
/// Differences from Camel: with a strategy that can pre-complete (<see cref="IAggregationStrategy.CanPreComplete"/>) the other
/// completion conditions are still checked after aggregating (Camel only checks <c>preComplete</c> and timeouts); there is no executor
/// service or timeout checker executor option, only <see cref="MaxDegreeOfParallelism"/>; <see cref="ForceCompletionOnStop"/> does not
/// apply when the route is only suspended; the batch consumer's properties are read from the incoming exchange.
/// </remarks>
public sealed class AggregateDefinition(IExpression correlationExpression) : OutputDefinition
{
    /// <summary>Computes the correlation key (converted to a string; null or blank keys are invalid).</summary>
    public IExpression CorrelationExpression { get; set; } = correlationExpression;

    /// <summary>Combines the exchanges of a group; null uses <see cref="AggregationStrategyRef"/>.</summary>
    public IAggregationStrategy? AggregationStrategy { get; set; }

    /// <summary>The name of an <see cref="IAggregationStrategy"/> in the registry (used when <see cref="AggregationStrategy"/> is null).</summary>
    public string? AggregationStrategyRef { get; set; }

    /// <summary>Stores the groups; null uses <see cref="AggregationRepositoryRef"/> or a new <see cref="MemoryAggregationRepository"/>.</summary>
    public IAggregationRepository? AggregationRepository { get; set; }

    /// <summary>The name of an <see cref="IAggregationRepository"/> in the registry.</summary>
    public string? AggregationRepositoryRef { get; set; }

    /// <inheritdoc cref="AggregateSettings.CompletionSize"/>
    public int CompletionSize { get; set; }

    /// <inheritdoc cref="AggregateSettings.CompletionSizeExpression"/>
    public IExpression? CompletionSizeExpression { get; set; }

    /// <inheritdoc cref="AggregateSettings.CompletionTimeout"/>
    public TimeSpan? CompletionTimeout { get; set; }

    /// <inheritdoc cref="AggregateSettings.CompletionTimeoutExpression"/>
    public IExpression? CompletionTimeoutExpression { get; set; }

    /// <inheritdoc cref="AggregateSettings.CompletionTimeoutCheckerInterval"/>
    public TimeSpan? CompletionTimeoutCheckerInterval { get; set; }

    /// <inheritdoc cref="AggregateSettings.CompletionInterval"/>
    public TimeSpan? CompletionInterval { get; set; }

    /// <inheritdoc cref="AggregateSettings.CompletionPredicate"/>
    public IPredicate? CompletionPredicate { get; set; }

    /// <inheritdoc cref="AggregateSettings.CompletionFromBatchConsumer"/>
    public bool CompletionFromBatchConsumer { get; set; }

    /// <inheritdoc cref="AggregateSettings.CompletionOnNewCorrelationGroup"/>
    public bool CompletionOnNewCorrelationGroup { get; set; }

    /// <inheritdoc cref="AggregateSettings.EagerCheckCompletion"/>
    public bool EagerCheckCompletion { get; set; }

    /// <inheritdoc cref="AggregateSettings.ForceCompletionOnStop"/>
    public bool ForceCompletionOnStop { get; set; }

    /// <inheritdoc cref="AggregateSettings.CompleteAllOnStop"/>
    public bool CompleteAllOnStop { get; set; }

    /// <inheritdoc cref="AggregateSettings.IgnoreInvalidCorrelationKeys"/>
    public bool IgnoreInvalidCorrelationKeys { get; set; }

    /// <inheritdoc cref="AggregateSettings.CloseCorrelationKeyOnCompletion"/>
    public int? CloseCorrelationKeyOnCompletion { get; set; }

    /// <inheritdoc cref="AggregateSettings.DiscardOnCompletionTimeout"/>
    public bool DiscardOnCompletionTimeout { get; set; }

    /// <inheritdoc cref="AggregateSettings.DiscardOnAggregationFailure"/>
    public bool DiscardOnAggregationFailure { get; set; }

    /// <inheritdoc cref="AggregateSettings.ParallelProcessing"/>
    public bool ParallelProcessing { get; set; }

    /// <summary>The maximum number of completed groups sent concurrently with parallel processing; 0 for the default (10).</summary>
    public int MaxDegreeOfParallelism { get; set; }

    /// <inheritdoc cref="AggregateSettings.OptimisticLocking"/>
    public bool OptimisticLocking { get; set; }

    /// <summary>How optimistic locking conflicts are retried; null for the defaults.</summary>
    public OptimisticLockRetryPolicy? OptimisticLockRetryPolicy { get; set; }

    /// <inheritdoc cref="AggregateSettings.AggregateController"/>
    public IAggregateController? AggregateController { get; set; }

    /// <inheritdoc />
    public override string ShortName => "aggregate";

    /// <inheritdoc />
    public override string Label => $"aggregate[{CorrelationExpression}]";

    /// <inheritdoc />
    public override IProcessor CreateProcessor(RouteContext routeContext)
    {
        var strategy = AggregationStrategy ?? Lookup<IAggregationStrategy>(routeContext, AggregationStrategyRef)
            ?? throw new InvalidOperationException("aggregate: an aggregation strategy must be set");
        var repository = AggregationRepository ?? Lookup<IAggregationRepository>(routeContext, AggregationRepositoryRef);
        if (MaxDegreeOfParallelism < 0)
        {
            throw new InvalidOperationException("aggregate: maxDegreeOfParallelism must not be negative");
        }

        var settings = new AggregateSettings
        {
            CompletionSize = CompletionSize,
            CompletionSizeExpression = CompletionSizeExpression,
            CompletionTimeout = CompletionTimeout,
            CompletionTimeoutExpression = CompletionTimeoutExpression,
            CompletionTimeoutCheckerInterval = CompletionTimeoutCheckerInterval ?? AggregateSettings.DefaultCompletionTimeoutCheckerInterval,
            CompletionInterval = CompletionInterval,
            CompletionPredicate = CompletionPredicate,
            CompletionFromBatchConsumer = CompletionFromBatchConsumer,
            CompletionOnNewCorrelationGroup = CompletionOnNewCorrelationGroup,
            EagerCheckCompletion = EagerCheckCompletion,
            ForceCompletionOnStop = ForceCompletionOnStop,
            CompleteAllOnStop = CompleteAllOnStop,
            IgnoreInvalidCorrelationKeys = IgnoreInvalidCorrelationKeys,
            CloseCorrelationKeyOnCompletion = CloseCorrelationKeyOnCompletion,
            DiscardOnCompletionTimeout = DiscardOnCompletionTimeout,
            DiscardOnAggregationFailure = DiscardOnAggregationFailure,
            ParallelProcessing = ParallelProcessing,
            MaxDegreeOfParallelism = MaxDegreeOfParallelism == 0 ? AggregateSettings.DefaultMaxDegreeOfParallelism : MaxDegreeOfParallelism,
            OptimisticLocking = OptimisticLocking,
            OptimisticLockRetryPolicy = OptimisticLockRetryPolicy ?? new OptimisticLockRetryPolicy(),
            AggregationRepository = repository,
            AggregateController = AggregateController,
        };
        var block = routeContext.CreateBlockProcessor(Outputs, this);
        return new AggregateProcessor(routeContext.Context, block, CorrelationExpression, strategy, settings, routeContext.RouteId);
    }

    private static T? Lookup<T>(RouteContext routeContext, string? name)
        where T : class => RegistryLookup.Lookup<T>(routeContext.Context, name);
}
