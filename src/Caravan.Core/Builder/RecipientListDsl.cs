using Caravan.Language;
using Caravan.Model;
using Caravan.Processors;
using Caravan.Support;

namespace Caravan.Builder;

/// <summary>DSL for the <c>recipientList</c> EIP.</summary>
public static class RecipientListDsl
{
    /// <summary>
    /// Sends a copy of the exchange to each recipient computed by <paramref name="recipients"/> (URIs separated by
    /// <paramref name="delimiter"/>, a sequence of URIs or endpoints, or an endpoint) and aggregates the replies:
    /// <c>.RecipientList(Expr.Header("recipients"))</c>. Branch properties: <see cref="ExchangePropertyKeys.RecipientListEndpoint"/>,
    /// <see cref="ExchangePropertyKeys.MulticastIndex"/>, <see cref="ExchangePropertyKeys.MulticastComplete"/>.
    /// </summary>
    /// <param name="steps">The block to add to.</param>
    /// <param name="recipients">Computes the recipients.</param>
    /// <param name="aggregationStrategy">Combines the branch results; null for the latest reply (<see cref="AggregationStrategies.UseLatest"/>).</param>
    /// <param name="aggregationStrategyRef">The name of an <see cref="IAggregationStrategy"/> in the registry, used when <paramref name="aggregationStrategy"/> is null.</param>
    /// <param name="parallelProcessing">Process the recipients concurrently on the thread pool (at most <paramref name="maxDegreeOfParallelism"/> at a time).</param>
    /// <param name="parallelAggregate">With parallel processing and streaming, call the strategy concurrently (it must be thread-safe).</param>
    /// <param name="maxDegreeOfParallelism">The maximum number of recipients processed concurrently; 0 for the default (10).</param>
    /// <param name="streaming">With parallel processing, aggregate the replies in the order they complete instead of the recipient order.</param>
    /// <param name="stopOnException">Stop at the first failed recipient: no further recipient is processed and the exchange fails with a <see cref="CaravanExchangeException"/> wrapping the failure.</param>
    /// <param name="timeout">With parallel processing, the total time to wait; unfinished recipients are then cancelled and ignored, and the strategy's <see cref="IAggregationStrategy.OnTimeout"/> is called for each.</param>
    /// <param name="onPrepare">Called on each recipient exchange before it is processed (e.g. to deep-copy a mutable body).</param>
    /// <param name="shareUnitOfWork">Recipients share the exchange's unit of work: their completion callbacks run when the exchange completes, and a failure is handled by the exchange's error handler (after the recipient's own redeliveries).</param>
    /// <param name="delimiter">Splits a string of recipients (default <c>,</c>); null to not split.</param>
    /// <param name="ignoreInvalidEndpoints">Skip recipients that cannot be resolved instead of failing the exchange.</param>
    /// <param name="cacheSize">Maximum cached producers (default 1000; negative disables caching).</param>
    public static T RecipientList<T>(
        this T steps,
        IExpression recipients,
        IAggregationStrategy? aggregationStrategy = null,
        string? aggregationStrategyRef = null,
        bool parallelProcessing = false,
        bool parallelAggregate = false,
        int maxDegreeOfParallelism = 0,
        bool streaming = false,
        bool stopOnException = false,
        TimeSpan? timeout = null,
        Action<Exchange>? onPrepare = null,
        bool shareUnitOfWork = false,
        string? delimiter = ",",
        bool ignoreInvalidEndpoints = false,
        int cacheSize = ProducerCache.DefaultCacheSize)
        where T : RouteSteps
    {
        var definition = steps.AddStep(new RecipientListDefinition(recipients)
        {
            Delimiter = delimiter,
            IgnoreInvalidEndpoints = ignoreInvalidEndpoints,
            CacheSize = cacheSize,
        });
        FanOutOptions.Apply(definition, aggregationStrategy, aggregationStrategyRef, parallelProcessing, parallelAggregate, maxDegreeOfParallelism, streaming, stopOnException, timeout, onPrepare, shareUnitOfWork);
        return steps;
    }

    /// <summary>Sends a copy of the exchange to each recipient returned by <paramref name="recipients"/>.</summary>
    /// <param name="steps">The block to add to.</param>
    /// <param name="recipients">Computes the recipients.</param>
    /// <param name="aggregationStrategy">Combines the branch results; null for the latest reply (<see cref="AggregationStrategies.UseLatest"/>).</param>
    /// <param name="aggregationStrategyRef">The name of an <see cref="IAggregationStrategy"/> in the registry, used when <paramref name="aggregationStrategy"/> is null.</param>
    /// <param name="parallelProcessing">Process the recipients concurrently on the thread pool (at most <paramref name="maxDegreeOfParallelism"/> at a time).</param>
    /// <param name="parallelAggregate">With parallel processing and streaming, call the strategy concurrently (it must be thread-safe).</param>
    /// <param name="maxDegreeOfParallelism">The maximum number of recipients processed concurrently; 0 for the default (10).</param>
    /// <param name="streaming">With parallel processing, aggregate the replies in the order they complete instead of the recipient order.</param>
    /// <param name="stopOnException">Stop at the first failed recipient: no further recipient is processed and the exchange fails with a <see cref="CaravanExchangeException"/> wrapping the failure.</param>
    /// <param name="timeout">With parallel processing, the total time to wait; unfinished recipients are then cancelled and ignored, and the strategy's <see cref="IAggregationStrategy.OnTimeout"/> is called for each.</param>
    /// <param name="onPrepare">Called on each recipient exchange before it is processed (e.g. to deep-copy a mutable body).</param>
    /// <param name="shareUnitOfWork">Recipients share the exchange's unit of work: their completion callbacks run when the exchange completes, and a failure is handled by the exchange's error handler (after the recipient's own redeliveries).</param>
    /// <param name="delimiter">Splits a string of recipients (default <c>,</c>); null to not split.</param>
    /// <param name="ignoreInvalidEndpoints">Skip recipients that cannot be resolved instead of failing the exchange.</param>
    /// <param name="cacheSize">Maximum cached producers (default 1000; negative disables caching).</param>
    public static T RecipientList<T>(
        this T steps,
        Func<Exchange, object?> recipients,
        IAggregationStrategy? aggregationStrategy = null,
        string? aggregationStrategyRef = null,
        bool parallelProcessing = false,
        bool parallelAggregate = false,
        int maxDegreeOfParallelism = 0,
        bool streaming = false,
        bool stopOnException = false,
        TimeSpan? timeout = null,
        Action<Exchange>? onPrepare = null,
        bool shareUnitOfWork = false,
        string? delimiter = ",",
        bool ignoreInvalidEndpoints = false,
        int cacheSize = ProducerCache.DefaultCacheSize)
        where T : RouteSteps
        => steps.RecipientList(new FuncExpression(recipients), aggregationStrategy, aggregationStrategyRef, parallelProcessing, parallelAggregate, maxDegreeOfParallelism, streaming, stopOnException, timeout, onPrepare, shareUnitOfWork, delimiter, ignoreInvalidEndpoints, cacheSize);

    /// <summary>
    /// Sends a copy of the exchange to each recipient of <paramref name="recipients"/>, which may contain <c>${header.x}</c>,
    /// <c>${body}</c>, <c>${exchangeProperty.x}</c> functions of the Simple language (see <see cref="SimpleLanguage"/>): <c>.RecipientList("mock:a,mock:${header.b}")</c>.
    /// </summary>
    /// <param name="steps">The block to add to.</param>
    /// <param name="recipients">The recipients template.</param>
    /// <param name="aggregationStrategy">Combines the branch results; null for the latest reply (<see cref="AggregationStrategies.UseLatest"/>).</param>
    /// <param name="aggregationStrategyRef">The name of an <see cref="IAggregationStrategy"/> in the registry, used when <paramref name="aggregationStrategy"/> is null.</param>
    /// <param name="parallelProcessing">Process the recipients concurrently on the thread pool (at most <paramref name="maxDegreeOfParallelism"/> at a time).</param>
    /// <param name="parallelAggregate">With parallel processing and streaming, call the strategy concurrently (it must be thread-safe).</param>
    /// <param name="maxDegreeOfParallelism">The maximum number of recipients processed concurrently; 0 for the default (10).</param>
    /// <param name="streaming">With parallel processing, aggregate the replies in the order they complete instead of the recipient order.</param>
    /// <param name="stopOnException">Stop at the first failed recipient: no further recipient is processed and the exchange fails with a <see cref="CaravanExchangeException"/> wrapping the failure.</param>
    /// <param name="timeout">With parallel processing, the total time to wait; unfinished recipients are then cancelled and ignored, and the strategy's <see cref="IAggregationStrategy.OnTimeout"/> is called for each.</param>
    /// <param name="onPrepare">Called on each recipient exchange before it is processed (e.g. to deep-copy a mutable body).</param>
    /// <param name="shareUnitOfWork">Recipients share the exchange's unit of work: their completion callbacks run when the exchange completes, and a failure is handled by the exchange's error handler (after the recipient's own redeliveries).</param>
    /// <param name="delimiter">Splits a string of recipients (default <c>,</c>); null to not split.</param>
    /// <param name="ignoreInvalidEndpoints">Skip recipients that cannot be resolved instead of failing the exchange.</param>
    /// <param name="cacheSize">Maximum cached producers (default 1000; negative disables caching).</param>
    public static T RecipientList<T>(
        this T steps,
        string recipients,
        IAggregationStrategy? aggregationStrategy = null,
        string? aggregationStrategyRef = null,
        bool parallelProcessing = false,
        bool parallelAggregate = false,
        int maxDegreeOfParallelism = 0,
        bool streaming = false,
        bool stopOnException = false,
        TimeSpan? timeout = null,
        Action<Exchange>? onPrepare = null,
        bool shareUnitOfWork = false,
        string? delimiter = ",",
        bool ignoreInvalidEndpoints = false,
        int cacheSize = ProducerCache.DefaultCacheSize)
        where T : RouteSteps
        => steps.RecipientList(SimpleLanguage.Expression(recipients), aggregationStrategy, aggregationStrategyRef, parallelProcessing, parallelAggregate, maxDegreeOfParallelism, streaming, stopOnException, timeout, onPrepare, shareUnitOfWork, delimiter, ignoreInvalidEndpoints, cacheSize);
}
