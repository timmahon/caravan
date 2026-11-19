using Caravan.Spi;
using Caravan.Support;

namespace Caravan.Processors;

/// <summary>
/// A least-recently-used cache of started <see cref="SendProcessor"/>s per endpoint, used by EIPs that send to endpoints computed per
/// exchange (<c>toD</c>, <c>wireTap</c>, <c>recipientList</c>), like Camel's <c>ProducerCache</c>. Evicted senders are stopped; the
/// owner calls <see cref="StopAsync"/> when it stops. Thread-safe.
/// </summary>
public sealed class ProducerCache
{
    /// <summary>The cache size used when none (0) is given.</summary>
    public const int DefaultCacheSize = 1000;

    private readonly Dictionary<string, LinkedListNode<SendProcessor>> _cache = new(StringComparer.Ordinal);
    private readonly LinkedList<SendProcessor> _lru = new();
    private readonly Lock _lock = new();

    /// <summary>Creates the cache.</summary>
    /// <param name="pattern">The exchange pattern senders use; null keeps the exchange's pattern.</param>
    /// <param name="cacheSize">Maximum cached senders; 0 uses <see cref="DefaultCacheSize"/>, negative disables caching.</param>
    public ProducerCache(ExchangePattern? pattern = null, int cacheSize = DefaultCacheSize)
    {
        Pattern = pattern;
        CacheSize = cacheSize == 0 ? DefaultCacheSize : cacheSize;
    }

    /// <summary>The exchange pattern senders use; null keeps the exchange's pattern.</summary>
    public ExchangePattern? Pattern { get; }

    /// <summary>Maximum number of cached senders; negative when caching is disabled.</summary>
    public int CacheSize { get; }

    /// <summary>The number of senders currently cached.</summary>
    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _cache.Count;
            }
        }
    }

    /// <summary>
    /// Resolves the endpoint for a value computed by an expression: an <see cref="IEndpoint"/> as is, anything else converted to a
    /// URI string (trimmed) and resolved through the context.
    /// </summary>
    /// <param name="exchange">The exchange the value was computed for.</param>
    /// <param name="value">The endpoint or URI.</param>
    /// <returns>The endpoint.</returns>
    /// <exception cref="ResolveEndpointFailedException">The value is null.</exception>
    public static IEndpoint ResolveEndpoint(Exchange exchange, object? value)
    {
        switch (value)
        {
            case IEndpoint endpoint:
                return endpoint;
            case null:
                throw new ResolveEndpointFailedException("null", "the endpoint URI expression returned null");
            default:
                var uri = ObjectHelper.ToText(exchange, value)!.Trim();
                return exchange.Context.GetEndpoint(uri);
        }
    }

    /// <summary>Sends <paramref name="exchange"/> to <paramref name="endpoint"/> with a cached (or, when caching is disabled, a temporary) sender.</summary>
    /// <param name="endpoint">The endpoint.</param>
    /// <param name="exchange">The exchange.</param>
    public async ValueTask SendAsync(IEndpoint endpoint, Exchange exchange)
    {
        if (CacheSize < 0)
        {
            var sender = new SendProcessor(endpoint, Pattern);
            await sender.StartAsync().ConfigureAwait(false);
            try
            {
                await sender.ProcessAsync(exchange).ConfigureAwait(false);
            }
            finally
            {
                await sender.StopAsync().ConfigureAwait(false);
            }

            return;
        }

        var cached = await AcquireAsync(endpoint).ConfigureAwait(false);
        await cached.ProcessAsync(exchange).ConfigureAwait(false);
    }

    /// <summary>Stops and removes all cached senders.</summary>
    /// <param name="cancellationToken">Cancels stopping.</param>
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        List<SendProcessor> senders;
        lock (_lock)
        {
            senders = [.. _lru];
            _lru.Clear();
            _cache.Clear();
        }

        foreach (var sender in senders)
        {
            await sender.StopAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public override string ToString() => $"ProducerCache[{Count}/{CacheSize}]";

    private async ValueTask<SendProcessor> AcquireAsync(IEndpoint endpoint)
    {
        var key = endpoint.EndpointKey;
        lock (_lock)
        {
            if (_cache.TryGetValue(key, out var node))
            {
                _lru.Remove(node);
                _lru.AddFirst(node);
                return node.Value;
            }
        }

        var created = new SendProcessor(endpoint, Pattern);
        await created.StartAsync().ConfigureAwait(false);

        SendProcessor result;
        SendProcessor? toStop = null;
        lock (_lock)
        {
            if (_cache.TryGetValue(key, out var existing))
            {
                // Another exchange created a sender for the same endpoint concurrently.
                toStop = created;
                result = existing.Value;
            }
            else
            {
                _cache[key] = _lru.AddFirst(created);
                result = created;
                if (_cache.Count > CacheSize)
                {
                    var last = _lru.Last!;
                    _lru.RemoveLast();
                    _cache.Remove(last.Value.Endpoint.EndpointKey);
                    toStop = last.Value;
                }
            }
        }

        if (toStop is not null)
        {
            await toStop.StopAsync().ConfigureAwait(false);
        }

        return result;
    }
}
