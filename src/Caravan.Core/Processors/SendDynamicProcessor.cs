using Caravan.Support;
using Microsoft.Extensions.Logging;

namespace Caravan.Processors;

/// <summary>
/// Sends to an endpoint computed per exchange (the <c>toD</c> EIP). The expression may return a URI string or an
/// <see cref="Spi.IEndpoint"/>. Senders are cached per endpoint in a <see cref="ProducerCache"/> of <see cref="CacheSize"/>
/// entries; evicted senders are stopped.
/// </summary>
public sealed class SendDynamicProcessor : ServiceSupport, Spi.IProcessor
{
    /// <summary>The cache size used when none (0) is given.</summary>
    public const int DefaultCacheSize = ProducerCache.DefaultCacheSize;

    private readonly ProducerCache _cache;

    /// <summary>Creates the processor.</summary>
    /// <param name="uri">Computes the endpoint URI (or endpoint) for each exchange.</param>
    /// <param name="pattern">The exchange pattern used while sending; null keeps the exchange's pattern.</param>
    /// <param name="cacheSize">Maximum cached senders; 0 uses <see cref="DefaultCacheSize"/>, negative disables caching.</param>
    /// <param name="ignoreInvalidEndpoint">Skip sending (instead of failing) when the URI cannot be resolved to an endpoint.</param>
    public SendDynamicProcessor(IExpression uri, ExchangePattern? pattern = null, int cacheSize = DefaultCacheSize, bool ignoreInvalidEndpoint = false)
    {
        Uri = uri;
        IgnoreInvalidEndpoint = ignoreInvalidEndpoint;
        _cache = new ProducerCache(pattern, cacheSize);
    }

    /// <summary>Computes the endpoint URI (or endpoint) for each exchange.</summary>
    public IExpression Uri { get; }

    /// <summary>The exchange pattern used while sending; null keeps the exchange's pattern.</summary>
    public ExchangePattern? Pattern => _cache.Pattern;

    /// <summary>Maximum number of cached senders; negative when caching is disabled.</summary>
    public int CacheSize => _cache.CacheSize;

    /// <summary>Whether an unresolvable URI is skipped instead of failing the exchange.</summary>
    public bool IgnoreInvalidEndpoint { get; }

    /// <summary>The number of senders currently cached.</summary>
    public int CachedCount => _cache.Count;

    /// <inheritdoc />
    public async ValueTask ProcessAsync(Exchange exchange)
    {
        var value = Uri.Evaluate(exchange);
        Spi.IEndpoint endpoint;
        try
        {
            endpoint = ProducerCache.ResolveEndpoint(exchange, value);
        }
        catch (Exception e) when (IgnoreInvalidEndpoint)
        {
            exchange.Context.LoggerFactory.CreateLogger<SendDynamicProcessor>()
                .LogDebug(e, "Ignoring invalid endpoint {Uri} for exchange {ExchangeId}", value, exchange.ExchangeId);
            return;
        }

        await _cache.SendAsync(endpoint, exchange).ConfigureAwait(false);
    }

    /// <summary>Stops and removes all cached senders.</summary>
    protected override Task DoStopAsync(CancellationToken cancellationToken) => _cache.StopAsync(cancellationToken);

    /// <inheritdoc />
    public override string ToString() => $"SendDynamic[{Uri}]";
}
