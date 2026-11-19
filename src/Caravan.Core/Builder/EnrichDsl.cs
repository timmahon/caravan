using Caravan.Language;
using Caravan.Model;
using Caravan.Processors;
using Caravan.Support;

namespace Caravan.Builder;

/// <summary>DSL for the <c>enrich</c> and <c>pollEnrich</c> EIPs.</summary>
public static class EnrichDsl
{
    /// <summary>
    /// Sends a copy of the exchange to the resource <paramref name="uri"/> (request/reply) and merges the reply into the exchange:
    /// <c>.Enrich("direct:resource", AggregationStrategies.From((original, resource) =&gt; ...))</c>. Without a strategy the exchange takes
    /// the reply (body and headers). The URI may contain <c>${header.x}</c>, <c>${body}</c>, <c>${exchangeProperty.x}</c> tokens
    /// (see <see cref="SimpleLanguage"/>).
    /// </summary>
    /// <param name="steps">The block to add to.</param>
    /// <param name="uri">The resource URI template.</param>
    /// <param name="aggregationStrategy">Merges the reply into the exchange (<c>Aggregate(original, reply)</c>); null to take the reply.</param>
    /// <param name="aggregationStrategyRef">The name of an <see cref="IAggregationStrategy"/> in the registry, used when <paramref name="aggregationStrategy"/> is null.</param>
    /// <param name="aggregateOnException">Give a failed reply to the strategy instead of failing the exchange.</param>
    /// <param name="shareUnitOfWork">The resource exchange shares the exchange's unit of work.</param>
    /// <param name="ignoreInvalidEndpoint">Skip, instead of failing, when the URI cannot be resolved.</param>
    /// <param name="cacheSize">Maximum cached producers (default 1000; negative disables caching).</param>
    public static T Enrich<T>(
        this T steps,
        string uri,
        IAggregationStrategy? aggregationStrategy = null,
        string? aggregationStrategyRef = null,
        bool aggregateOnException = false,
        bool shareUnitOfWork = false,
        bool ignoreInvalidEndpoint = false,
        int cacheSize = ProducerCache.DefaultCacheSize)
        where T : RouteSteps =>
        steps.Enrich(SimpleLanguage.Expression(uri), aggregationStrategy, aggregationStrategyRef, aggregateOnException, shareUnitOfWork, ignoreInvalidEndpoint, cacheSize);

    /// <summary>Sends a copy of the exchange to the resource computed by <paramref name="uri"/> (request/reply) and merges the reply into the exchange.</summary>
    /// <param name="steps">The block to add to.</param>
    /// <param name="uri">Computes the resource URI (or endpoint).</param>
    /// <param name="aggregationStrategy">Merges the reply into the exchange (<c>Aggregate(original, reply)</c>); null to take the reply.</param>
    /// <param name="aggregationStrategyRef">The name of an <see cref="IAggregationStrategy"/> in the registry, used when <paramref name="aggregationStrategy"/> is null.</param>
    /// <param name="aggregateOnException">Give a failed reply to the strategy instead of failing the exchange.</param>
    /// <param name="shareUnitOfWork">The resource exchange shares the exchange's unit of work.</param>
    /// <param name="ignoreInvalidEndpoint">Skip, instead of failing, when the URI cannot be resolved.</param>
    /// <param name="cacheSize">Maximum cached producers (default 1000; negative disables caching).</param>
    public static T Enrich<T>(
        this T steps,
        IExpression uri,
        IAggregationStrategy? aggregationStrategy = null,
        string? aggregationStrategyRef = null,
        bool aggregateOnException = false,
        bool shareUnitOfWork = false,
        bool ignoreInvalidEndpoint = false,
        int cacheSize = ProducerCache.DefaultCacheSize)
        where T : RouteSteps
    {
        steps.AddStep(new EnrichDefinition(uri)
        {
            AggregationStrategy = aggregationStrategy,
            AggregationStrategyRef = aggregationStrategyRef,
            AggregateOnException = aggregateOnException,
            ShareUnitOfWork = shareUnitOfWork,
            IgnoreInvalidEndpoint = ignoreInvalidEndpoint,
            CacheSize = cacheSize,
        });
        return steps;
    }

    /// <summary>
    /// Receives a message from the resource <paramref name="uri"/> with a polling consumer and merges it into the exchange:
    /// <c>.PollEnrich("seda:resource", timeout: TimeSpan.FromSeconds(5))</c>. Without a strategy the exchange takes the received message,
    /// or gets a null body when nothing was received in time. The URI may contain <c>${...}</c> functions of the Simple language (see <see cref="SimpleLanguage"/>).
    /// </summary>
    /// <param name="steps">The block to add to.</param>
    /// <param name="uri">The resource URI template.</param>
    /// <param name="timeout">How long to wait for a message: null or negative (the default, as Camel's <c>-1</c>) blocks until one arrives, <see cref="TimeSpan.Zero"/> does not wait.</param>
    /// <param name="aggregationStrategy">Merges the received message into the exchange; null to take it.</param>
    /// <param name="aggregationStrategyRef">The name of an <see cref="IAggregationStrategy"/> in the registry, used when <paramref name="aggregationStrategy"/> is null.</param>
    /// <param name="aggregateOnException">Give a failed received exchange to the strategy instead of failing the exchange.</param>
    /// <param name="ignoreInvalidEndpoint">Skip, instead of failing, when the URI cannot be resolved.</param>
    /// <param name="cacheSize">Maximum cached polling consumers (default 1000; negative disables caching).</param>
    public static T PollEnrich<T>(
        this T steps,
        string uri,
        TimeSpan? timeout = null,
        IAggregationStrategy? aggregationStrategy = null,
        string? aggregationStrategyRef = null,
        bool aggregateOnException = false,
        bool ignoreInvalidEndpoint = false,
        int cacheSize = ConsumerCache.DefaultCacheSize)
        where T : RouteSteps =>
        steps.PollEnrich(SimpleLanguage.Expression(uri), timeout, aggregationStrategy, aggregationStrategyRef, aggregateOnException, ignoreInvalidEndpoint, cacheSize);

    /// <summary>Receives a message from the resource computed by <paramref name="uri"/> with a polling consumer and merges it into the exchange.</summary>
    /// <param name="steps">The block to add to.</param>
    /// <param name="uri">Computes the resource URI (or endpoint).</param>
    /// <param name="timeout">How long to wait for a message: null or negative (the default, as Camel's <c>-1</c>) blocks until one arrives, <see cref="TimeSpan.Zero"/> does not wait.</param>
    /// <param name="aggregationStrategy">Merges the received message into the exchange; null to take it.</param>
    /// <param name="aggregationStrategyRef">The name of an <see cref="IAggregationStrategy"/> in the registry, used when <paramref name="aggregationStrategy"/> is null.</param>
    /// <param name="aggregateOnException">Give a failed received exchange to the strategy instead of failing the exchange.</param>
    /// <param name="ignoreInvalidEndpoint">Skip, instead of failing, when the URI cannot be resolved.</param>
    /// <param name="cacheSize">Maximum cached polling consumers (default 1000; negative disables caching).</param>
    public static T PollEnrich<T>(
        this T steps,
        IExpression uri,
        TimeSpan? timeout = null,
        IAggregationStrategy? aggregationStrategy = null,
        string? aggregationStrategyRef = null,
        bool aggregateOnException = false,
        bool ignoreInvalidEndpoint = false,
        int cacheSize = ConsumerCache.DefaultCacheSize)
        where T : RouteSteps
    {
        steps.AddStep(new PollEnrichDefinition(uri)
        {
            Timeout = timeout,
            AggregationStrategy = aggregationStrategy,
            AggregationStrategyRef = aggregationStrategyRef,
            AggregateOnException = aggregateOnException,
            IgnoreInvalidEndpoint = ignoreInvalidEndpoint,
            CacheSize = cacheSize,
        });
        return steps;
    }
}
