using Caravan.Spi;
using Caravan.Support;

namespace Caravan.Processors;

/// <summary>
/// A least-recently-used cache of started <see cref="IPollingConsumer"/>s per endpoint (Camel's <c>ConsumerCache</c>), used by
/// <c>pollEnrich</c>. Evicted consumers are stopped; the owner calls <see cref="StopAsync"/> when it stops. Thread-safe.
/// </summary>
public sealed class ConsumerCache
{
    /// <summary>The cache size used when none (0) is given.</summary>
    public const int DefaultCacheSize = 1000;

    private readonly Dictionary<string, LinkedListNode<IPollingConsumer>> _cache = new(StringComparer.Ordinal);
    private readonly LinkedList<IPollingConsumer> _lru = new();
    private readonly Lock _lock = new();

    /// <summary>Creates the cache.</summary>
    /// <param name="cacheSize">Maximum cached consumers; 0 uses <see cref="DefaultCacheSize"/>, negative disables caching.</param>
    public ConsumerCache(int cacheSize = DefaultCacheSize)
    {
        CacheSize = cacheSize == 0 ? DefaultCacheSize : cacheSize;
    }

    /// <summary>Maximum number of cached consumers; negative when caching is disabled.</summary>
    public int CacheSize { get; }

    /// <summary>The number of consumers currently cached.</summary>
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
    /// Polls <paramref name="endpoint"/> with a cached (or, when caching is disabled, a temporary) polling consumer.
    /// </summary>
    /// <param name="endpoint">The endpoint.</param>
    /// <param name="receive">Receives from the consumer.</param>
    /// <returns>What <paramref name="receive"/> returned.</returns>
    public async Task<Exchange?> ReceiveAsync(IEndpoint endpoint, Func<IPollingConsumer, Task<Exchange?>> receive)
    {
        ArgumentNullException.ThrowIfNull(receive);
        if (CacheSize < 0)
        {
            await endpoint.StartAsync().ConfigureAwait(false);
            var consumer = PollingConsumerFactory.Create(endpoint);
            await consumer.StartAsync().ConfigureAwait(false);
            try
            {
                return await receive(consumer).ConfigureAwait(false);
            }
            finally
            {
                await consumer.StopAsync().ConfigureAwait(false);
            }
        }

        var cached = await AcquireAsync(endpoint).ConfigureAwait(false);
        return await receive(cached).ConfigureAwait(false);
    }

    /// <summary>Stops and removes all cached consumers.</summary>
    /// <param name="cancellationToken">Cancels stopping.</param>
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        List<IPollingConsumer> consumers;
        lock (_lock)
        {
            consumers = [.. _lru];
            _lru.Clear();
            _cache.Clear();
        }

        foreach (var consumer in consumers)
        {
            await consumer.StopAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public override string ToString() => $"ConsumerCache[{Count}/{CacheSize}]";

    private async ValueTask<IPollingConsumer> AcquireAsync(IEndpoint endpoint)
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

        await endpoint.StartAsync().ConfigureAwait(false);
        var created = PollingConsumerFactory.Create(endpoint);
        await created.StartAsync().ConfigureAwait(false);

        IPollingConsumer result;
        IPollingConsumer? toStop = null;
        lock (_lock)
        {
            if (_cache.TryGetValue(key, out var existing))
            {
                // Another exchange created a consumer for the same endpoint concurrently.
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
