using Caravan.Aggregation;
using Caravan.Spi;
using Caravan.Support;
using Microsoft.Extensions.Logging;

namespace Caravan.Processors;

/// <summary>
/// The <c>enrich</c> EIP (Camel's <c>Enricher</c>): sends a correlated copy of the exchange to the resource endpoint computed by
/// <see cref="Uri"/> as request/reply (<c>InOut</c>) and merges the reply into the exchange with <see cref="AggregationStrategy"/>
/// (by default <see cref="CopyAggregationStrategy"/>: the exchange takes the reply). <see cref="ExchangePropertyKeys.ToEndpoint"/> is set
/// to the resource URI.
/// </summary>
/// <remarks>
/// When the resource fails the exchange takes its result, failure included, unless <see cref="AggregateOnException"/>: the strategy then
/// receives the failed reply and decides. An exception thrown by the strategy fails the exchange with a <see cref="CaravanExchangeException"/>
/// ("Error occurred during aggregation"). The resource exchange has its own unit of work unless <see cref="ShareUnitOfWork"/>; completion
/// callbacks left on it are handed over to the exchange.
/// </remarks>
public sealed class EnrichProcessor : ServiceSupport, IProcessor
{
    private readonly ProducerCache _cache;

    /// <summary>Creates the processor.</summary>
    /// <param name="uri">Computes the resource URI (or endpoint).</param>
    /// <param name="aggregationStrategy">Merges the reply into the exchange; null for <see cref="CopyAggregationStrategy"/>.</param>
    /// <param name="aggregateOnException">Whether a failed reply is given to the strategy instead of failing the exchange.</param>
    /// <param name="shareUnitOfWork">Whether the resource exchange shares the exchange's unit of work.</param>
    /// <param name="ignoreInvalidEndpoint">Skip (leaving the exchange unchanged) when the URI cannot be resolved.</param>
    /// <param name="cacheSize">Maximum cached producers; 0 uses the default, negative disables caching.</param>
    public EnrichProcessor(
        IExpression uri,
        IAggregationStrategy? aggregationStrategy = null,
        bool aggregateOnException = false,
        bool shareUnitOfWork = false,
        bool ignoreInvalidEndpoint = false,
        int cacheSize = ProducerCache.DefaultCacheSize)
    {
        Uri = uri;
        AggregationStrategy = aggregationStrategy ?? CopyAggregationStrategy.Instance;
        AggregateOnException = aggregateOnException;
        ShareUnitOfWork = shareUnitOfWork;
        IgnoreInvalidEndpoint = ignoreInvalidEndpoint;
        _cache = new ProducerCache(ExchangePattern.InOut, cacheSize);
    }

    /// <summary>Computes the resource URI (or endpoint).</summary>
    public IExpression Uri { get; }

    /// <summary>Merges the reply into the exchange.</summary>
    public IAggregationStrategy AggregationStrategy { get; }

    /// <summary>Whether a failed reply is given to the strategy instead of failing the exchange.</summary>
    public bool AggregateOnException { get; }

    /// <summary>Whether the resource exchange shares the exchange's unit of work.</summary>
    public bool ShareUnitOfWork { get; }

    /// <summary>Whether an unresolvable URI is skipped instead of failing the exchange.</summary>
    public bool IgnoreInvalidEndpoint { get; }

    /// <summary>The variable whose value is sent as the resource's body (Camel's <c>variableSend</c>).</summary>
    public string? VariableSend { get; init; }

    /// <summary>
    /// The variable receiving the reply (Camel's <c>variableReceive</c>: body as the variable, headers as <c>name.header.key</c>); the aggregation
    /// strategy is then not used and the message is left unchanged.
    /// </summary>
    public string? VariableReceive { get; init; }

    /// <summary>Maximum number of cached producers; negative when caching is disabled.</summary>
    public int CacheSize => _cache.CacheSize;

    /// <inheritdoc />
    public async ValueTask ProcessAsync(Exchange exchange)
    {
        var value = Uri.Evaluate(exchange);
        IEndpoint endpoint;
        try
        {
            endpoint = ProducerCache.ResolveEndpoint(exchange, value);
        }
        catch (Exception e) when (IgnoreInvalidEndpoint)
        {
            exchange.Context.LoggerFactory.CreateLogger<EnrichProcessor>()
                .LogDebug(e, "Ignoring invalid endpoint {Uri} for exchange {ExchangeId}", value, exchange.ExchangeId);
            return;
        }

        var resource = exchange.CreateCorrelatedCopy();
        resource.Pattern = ExchangePattern.InOut;
        if (VariableSend is not null)
        {
            resource.Message.Body = exchange.GetVariable(VariableSend);
        }

        if (ShareUnitOfWork)
        {
            resource.UnitOfWork = exchange.UnitOfWork;
        }

        try
        {
            await _cache.SendAsync(endpoint, resource).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            resource.Exception = e;
        }

        if (ShareUnitOfWork)
        {
            resource.UnitOfWork = null;
        }

        if (VariableReceive is not null && !resource.IsFailed)
        {
            VariableSendReceive.Store(exchange, VariableReceive, resource.Message);
            ExchangeHelper.HandoverCompletions(resource, exchange);
        }
        else
        {
            Merge(exchange, resource, AggregationStrategy, AggregateOnException);
        }

        exchange.SetProperty(ExchangePropertyKeys.ToEndpoint, endpoint.EndpointUri);
    }

    /// <inheritdoc />
    public override string ToString() => $"Enrich[{Uri}]";

    /// <summary>Merges the resource's reply (null when nothing was received) into <paramref name="exchange"/>, as Camel's enrichers do.</summary>
    internal static void Merge(Exchange exchange, Exchange? resource, IAggregationStrategy strategy, bool aggregateOnException)
    {
        if (resource is { IsFailed: true } && !aggregateOnException)
        {
            ExchangeHelper.CopyResults(exchange, resource);
            return;
        }

        try
        {
            var aggregated = resource is null ? strategy.AggregateNoMessage(exchange) : strategy.Aggregate(exchange, resource);
            if (aggregated is null)
            {
                return;
            }

            ExchangeHelper.CopyResults(exchange, aggregated);
            if (resource is not null)
            {
                ExchangeHelper.HandoverCompletions(resource, exchange);
            }
        }
        catch (Exception e)
        {
            exchange.Exception = new CaravanExchangeException("Error occurred during aggregation", exchange, e);
        }
    }

    /// <summary>Stops the cached producers.</summary>
    protected override Task DoStopAsync(CancellationToken cancellationToken) => _cache.StopAsync(cancellationToken);
}
