using Caravan.Aggregation;
using Caravan.Spi;
using Caravan.Support;
using Microsoft.Extensions.Logging;

namespace Caravan.Processors;

/// <summary>
/// The <c>pollEnrich</c> EIP (Camel's <c>PollEnricher</c>): receives a message from the resource endpoint computed by <see cref="Uri"/> with a
/// polling consumer (see <see cref="PollingConsumerFactory"/>) and merges it into the exchange with <see cref="AggregationStrategy"/> (by default
/// <see cref="CopyAggregationStrategy"/>: the exchange takes the received message). When nothing is received within <see cref="Timeout"/> the
/// strategy's <see cref="IAggregationStrategy.AggregateNoMessage"/> is called (the default sets the body to null).
/// </summary>
/// <remarks>
/// <see cref="Timeout"/>: null or negative waits until a message arrives (or the exchange is cancelled), zero takes a message only if one is
/// immediately available, positive waits that long on the context's clock. A received exchange that failed fails the exchange unless
/// <see cref="AggregateOnException"/>. Completion callbacks of the received exchange are handed over to the exchange. Polling consumers are
/// cached per endpoint in a <see cref="ConsumerCache"/>; an error while polling fails the exchange with a <see cref="CaravanExchangeException"/>.
/// </remarks>
public sealed class PollEnrichProcessor : ServiceSupport, IProcessor
{
    private readonly ConsumerCache _cache;

    /// <summary>Creates the processor.</summary>
    /// <param name="uri">Computes the resource URI (or endpoint).</param>
    /// <param name="timeout">How long to wait for a message: null or negative to block, zero to not wait.</param>
    /// <param name="aggregationStrategy">Merges the received message into the exchange; null for <see cref="CopyAggregationStrategy"/>.</param>
    /// <param name="aggregateOnException">Whether a failed received exchange is given to the strategy instead of failing the exchange.</param>
    /// <param name="ignoreInvalidEndpoint">Skip (leaving the exchange unchanged) when the URI cannot be resolved.</param>
    /// <param name="cacheSize">Maximum cached consumers; 0 uses the default, negative disables caching.</param>
    public PollEnrichProcessor(
        IExpression uri,
        TimeSpan? timeout = null,
        IAggregationStrategy? aggregationStrategy = null,
        bool aggregateOnException = false,
        bool ignoreInvalidEndpoint = false,
        int cacheSize = ConsumerCache.DefaultCacheSize)
    {
        Uri = uri;
        Timeout = timeout is { } t && t < TimeSpan.Zero ? null : timeout;
        AggregationStrategy = aggregationStrategy ?? CopyAggregationStrategy.Instance;
        AggregateOnException = aggregateOnException;
        IgnoreInvalidEndpoint = ignoreInvalidEndpoint;
        _cache = new ConsumerCache(cacheSize);
    }

    /// <summary>Computes the resource URI (or endpoint).</summary>
    public IExpression Uri { get; }

    /// <summary>How long to wait for a message: null to block until one arrives, zero to not wait.</summary>
    public TimeSpan? Timeout { get; }

    /// <summary>Merges the received message into the exchange.</summary>
    public IAggregationStrategy AggregationStrategy { get; }

    /// <summary>Whether a failed received exchange is given to the strategy instead of failing the exchange.</summary>
    public bool AggregateOnException { get; }

    /// <summary>Whether an unresolvable URI is skipped instead of failing the exchange.</summary>
    public bool IgnoreInvalidEndpoint { get; }

    /// <summary>Maximum number of cached consumers; negative when caching is disabled.</summary>
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
            exchange.Context.LoggerFactory.CreateLogger<PollEnrichProcessor>()
                .LogDebug(e, "Ignoring invalid endpoint {Uri} for exchange {ExchangeId}", value, exchange.ExchangeId);
            return;
        }

        Exchange? received;
        try
        {
            received = await _cache.ReceiveAsync(endpoint, ReceiveAsync).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            exchange.Exception = new CaravanExchangeException($"Error during pollEnrich from {endpoint.EndpointUri}", exchange, e);
            return;
        }

        EnrichProcessor.Merge(exchange, received, AggregationStrategy, AggregateOnException);
        exchange.SetProperty(ExchangePropertyKeys.ToEndpoint, endpoint.EndpointUri);

        async Task<Exchange?> ReceiveAsync(IPollingConsumer consumer) => Timeout switch
        {
            null => await consumer.ReceiveAsync(exchange.CancellationToken).ConfigureAwait(false),
            { } t when t == TimeSpan.Zero => consumer.ReceiveNoWait(),
            { } t => await consumer.ReceiveAsync(t, exchange.CancellationToken).ConfigureAwait(false),
        };
    }

    /// <inheritdoc />
    public override string ToString() => $"PollEnrich[{Uri}]";

    /// <summary>Stops the cached consumers.</summary>
    protected override Task DoStopAsync(CancellationToken cancellationToken) => _cache.StopAsync(cancellationToken);
}
