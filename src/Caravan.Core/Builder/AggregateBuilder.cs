using Caravan.Aggregation;
using Caravan.Model;
using Caravan.Processors;
using Caravan.Spi;

namespace Caravan.Builder;

/// <summary>
/// Configures an <c>aggregate</c> EIP: <c>.Aggregate(Expr.Header("id"), AggregationStrategies.GroupedBody(), a =&gt; a.CompletionSize(3).CompletionTimeout(TimeSpan.FromSeconds(5)), then =&gt; then.To("mock:result"))</c>.
/// At least one completion condition is required. See <see cref="AggregateSettings"/> for the semantics of each option.
/// </summary>
public sealed class AggregateBuilder
{
    /// <summary>Creates a builder for <paramref name="definition"/>.</summary>
    /// <param name="definition">The definition to configure.</param>
    public AggregateBuilder(AggregateDefinition definition)
    {
        Definition = definition;
    }

    /// <summary>The definition being configured.</summary>
    public AggregateDefinition Definition { get; }

    /// <summary>Completes a group once it holds <paramref name="size"/> exchanges.</summary>
    /// <param name="size">The number of exchanges.</param>
    public AggregateBuilder CompletionSize(int size)
    {
        Definition.CompletionSize = size;
        return this;
    }

    /// <summary>Completes a group once it holds as many exchanges as the expression yields (evaluated on the aggregated exchange).</summary>
    /// <param name="size">Computes the size; a non-positive or null value falls back to the fixed size.</param>
    public AggregateBuilder CompletionSize(IExpression size)
    {
        Definition.CompletionSizeExpression = size;
        return this;
    }

    /// <summary>Completes a group once it holds as many exchanges as the function returns.</summary>
    /// <param name="size">Computes the size; a non-positive or null value falls back to the fixed size.</param>
    public AggregateBuilder CompletionSize(Func<Exchange, object?> size) => CompletionSize(new FuncExpression(size));

    /// <summary>Completes a group that received no exchange for <paramref name="timeout"/> (each exchange restarts it).</summary>
    /// <param name="timeout">The inactivity timeout.</param>
    public AggregateBuilder CompletionTimeout(TimeSpan timeout)
    {
        Definition.CompletionTimeout = timeout;
        return this;
    }

    /// <summary>Completes a group that received no exchange for the timeout the expression yields (a <see cref="TimeSpan"/> or milliseconds).</summary>
    /// <param name="timeout">Computes the timeout on each incoming exchange.</param>
    public AggregateBuilder CompletionTimeout(IExpression timeout)
    {
        Definition.CompletionTimeoutExpression = timeout;
        return this;
    }

    /// <summary>Completes a group that received no exchange for the timeout the function returns (a <see cref="TimeSpan"/> or milliseconds).</summary>
    /// <param name="timeout">Computes the timeout on each incoming exchange.</param>
    public AggregateBuilder CompletionTimeout(Func<Exchange, object?> timeout) => CompletionTimeout(new FuncExpression(timeout));

    /// <summary>How often completion timeouts are checked (default 1 s).</summary>
    /// <param name="interval">The check interval.</param>
    public AggregateBuilder CompletionTimeoutCheckerInterval(TimeSpan interval)
    {
        Definition.CompletionTimeoutCheckerInterval = interval;
        return this;
    }

    /// <summary>Completes all groups every <paramref name="interval"/>.</summary>
    /// <param name="interval">The period.</param>
    public AggregateBuilder CompletionInterval(TimeSpan interval)
    {
        Definition.CompletionInterval = interval;
        return this;
    }

    /// <summary>Completes a group when the predicate matches the aggregated exchange.</summary>
    /// <param name="predicate">The completion predicate.</param>
    public AggregateBuilder CompletionPredicate(IPredicate predicate)
    {
        Definition.CompletionPredicate = predicate;
        return this;
    }

    /// <summary>Completes a group when the predicate matches the aggregated exchange.</summary>
    /// <param name="predicate">The completion predicate.</param>
    public AggregateBuilder CompletionPredicate(Func<Exchange, bool> predicate) => CompletionPredicate(Pred.From(predicate));

    /// <summary>Completes a group when the predicate matches the aggregated body: <c>a =&gt; a.CompletionPredicate((string s) =&gt; s.EndsWith('.'))</c>.</summary>
    /// <typeparam name="TBody">The type the body is converted to.</typeparam>
    /// <param name="predicate">The completion predicate.</param>
    public AggregateBuilder CompletionPredicate<TBody>(Func<TBody, bool> predicate) => CompletionPredicate(Pred.Body(predicate));

    /// <summary>Completes the groups of a batch when the batch consumer's batch is done (<see cref="ExchangePropertyKeys.BatchSize"/>, <see cref="ExchangePropertyKeys.BatchComplete"/>).</summary>
    /// <param name="enabled">Whether the option is on.</param>
    public AggregateBuilder CompletionFromBatchConsumer(bool enabled = true)
    {
        Definition.CompletionFromBatchConsumer = enabled;
        return this;
    }

    /// <summary>Completes all other groups when an exchange starts a new group.</summary>
    /// <param name="enabled">Whether the option is on.</param>
    public AggregateBuilder CompletionOnNewCorrelationGroup(bool enabled = true)
    {
        Definition.CompletionOnNewCorrelationGroup = enabled;
        return this;
    }

    /// <summary>Checks completion on the incoming exchange, before it is aggregated.</summary>
    /// <param name="enabled">Whether the option is on.</param>
    public AggregateBuilder EagerCheckCompletion(bool enabled = true)
    {
        Definition.EagerCheckCompletion = enabled;
        return this;
    }

    /// <summary>Completes all groups when the route is stopped.</summary>
    /// <param name="enabled">Whether the option is on.</param>
    public AggregateBuilder ForceCompletionOnStop(bool enabled = true)
    {
        Definition.ForceCompletionOnStop = enabled;
        return this;
    }

    /// <summary>Makes a graceful stop wait until the stored groups completed (by timeout or interval).</summary>
    /// <param name="enabled">Whether the option is on.</param>
    public AggregateBuilder CompleteAllOnStop(bool enabled = true)
    {
        Definition.CompleteAllOnStop = enabled;
        return this;
    }

    /// <summary>Ignores exchanges whose correlation key is null or blank instead of failing them.</summary>
    /// <param name="enabled">Whether the option is on.</param>
    public AggregateBuilder IgnoreInvalidCorrelationKeys(bool enabled = true)
    {
        Definition.IgnoreInvalidCorrelationKeys = enabled;
        return this;
    }

    /// <summary>Rejects exchanges for a correlation key whose group completed with a <see cref="ClosedCorrelationKeyException"/>.</summary>
    /// <param name="capacity">The number of closed keys remembered (oldest forgotten first); 0 for no limit.</param>
    public AggregateBuilder CloseCorrelationKeyOnCompletion(int capacity)
    {
        Definition.CloseCorrelationKeyOnCompletion = capacity;
        return this;
    }

    /// <summary>Discards groups completed by timeout instead of sending them.</summary>
    /// <param name="enabled">Whether the option is on.</param>
    public AggregateBuilder DiscardOnCompletionTimeout(bool enabled = true)
    {
        Definition.DiscardOnCompletionTimeout = enabled;
        return this;
    }

    /// <summary>Discards a group (and the incoming exchange) when the strategy throws, instead of failing the incoming exchange.</summary>
    /// <param name="enabled">Whether the option is on.</param>
    public AggregateBuilder DiscardOnAggregationFailure(bool enabled = true)
    {
        Definition.DiscardOnAggregationFailure = enabled;
        return this;
    }

    /// <summary>Lets application code force the completion or discarding of groups.</summary>
    /// <param name="controller">The controller, e.g. a <see cref="DefaultAggregateController"/>.</param>
    public AggregateBuilder AggregateController(IAggregateController controller)
    {
        Definition.AggregateController = controller;
        return this;
    }

    /// <summary>Sends completed groups on the thread pool.</summary>
    /// <param name="enabled">Whether the option is on.</param>
    /// <param name="maxDegreeOfParallelism">The maximum number of groups sent concurrently; 0 for the default (10).</param>
    public AggregateBuilder ParallelProcessing(bool enabled = true, int maxDegreeOfParallelism = 0)
    {
        Definition.ParallelProcessing = enabled;
        Definition.MaxDegreeOfParallelism = maxDegreeOfParallelism;
        return this;
    }

    /// <summary>Aggregates without a lock, retrying when the repository detects a concurrent update.</summary>
    /// <param name="enabled">Whether the option is on.</param>
    /// <param name="retryPolicy">How conflicts are retried; null for the defaults.</param>
    public AggregateBuilder OptimisticLocking(bool enabled = true, OptimisticLockRetryPolicy? retryPolicy = null)
    {
        Definition.OptimisticLocking = enabled;
        Definition.OptimisticLockRetryPolicy = retryPolicy ?? Definition.OptimisticLockRetryPolicy;
        return this;
    }

    /// <summary>Stores the groups in <paramref name="repository"/> instead of a new <see cref="MemoryAggregationRepository"/>.</summary>
    /// <param name="repository">The repository.</param>
    public AggregateBuilder AggregationRepository(IAggregationRepository repository)
    {
        Definition.AggregationRepository = repository;
        return this;
    }

    /// <summary>Stores the groups in the <see cref="IAggregationRepository"/> bound in the registry under <paramref name="name"/>.</summary>
    /// <param name="name">The bean name.</param>
    public AggregateBuilder AggregationRepositoryRef(string name)
    {
        Definition.AggregationRepositoryRef = name;
        return this;
    }

    /// <summary>Uses the <see cref="IAggregationStrategy"/> bound in the registry under <paramref name="name"/> when no strategy is given.</summary>
    /// <param name="name">The bean name.</param>
    public AggregateBuilder AggregationStrategyRef(string name)
    {
        Definition.AggregationStrategyRef = name;
        return this;
    }
}
