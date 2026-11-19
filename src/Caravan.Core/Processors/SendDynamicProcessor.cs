using Caravan.Spi;
using Caravan.Support;
using Microsoft.Extensions.Logging;

namespace Caravan.Processors;

/// <summary>
/// Sends to an endpoint computed per exchange (the <c>toD</c> EIP). The expression may return a URI string or an
/// <see cref="IEndpoint"/>. Senders are cached per endpoint in a least-recently-used cache of
/// <see cref="CacheSize"/> entries; evicted senders are stopped.
/// </summary>
public sealed class SendDynamicProcessor : ServiceSupport, IProcessor
{
    /// <summary>The cache size used when none (0) is given.</summary>
    public const int DefaultCacheSize = 1000;

    private readonly Dictionary<string, LinkedListNode<SendProcessor>> _cache = new(StringComparer.Ordinal);
    private readonly LinkedList<SendProcessor> _lru = new();
    private readonly Lock _lock = new();

    /// <summary>Creates the processor.</summary>
    /// <param name="uri">Computes the endpoint URI (or endpoint) for each exchange.</param>
    /// <param name="pattern">The exchange pattern used while sending; null keeps the exchange's pattern.</param>
    /// <param name="cacheSize">Maximum cached senders; 0 uses <see cref="DefaultCacheSize"/>, negative disables caching.</param>
    /// <param name="ignoreInvalidEndpoint">Skip sending (instead of failing) when the URI cannot be resolved to an endpoint.</param>
    public SendDynamicProcessor(IExpression uri, ExchangePattern? pattern = null, int cacheSize = DefaultCacheSize, bool ignoreInvalidEndpoint = false)
    {
        Uri = uri;
        Pattern = pattern;
        CacheSize = cacheSize == 0 ? DefaultCacheSize : cacheSize;
        IgnoreInvalidEndpoint = ignoreInvalidEndpoint;
    }

    /// <summary>Computes the endpoint URI (or endpoint) for each exchange.</summary>
    public IExpression Uri { get; }

    /// <summary>The exchange pattern used while sending; null keeps the exchange's pattern.</summary>
    public ExchangePattern? Pattern { get; }

    /// <summary>Maximum number of cached senders; negative when caching is disabled.</summary>
    public int CacheSize { get; }

    /// <summary>Whether an unresolvable URI is skipped instead of failing the exchange.</summary>
    public bool IgnoreInvalidEndpoint { get; }

    /// <summary>The number of senders currently cached.</summary>
    public int CachedCount
    {
        get
        {
            lock (_lock)
            {
                return _cache.Count;
            }
        }
    }

    /// <inheritdoc />
    public async ValueTask ProcessAsync(Exchange exchange)
    {
        var value = Uri.Evaluate(exchange);
        IEndpoint endpoint;
        try
        {
            endpoint = ResolveEndpoint(exchange, value);
        }
        catch (Exception e) when (IgnoreInvalidEndpoint)
        {
            exchange.Context.LoggerFactory.CreateLogger<SendDynamicProcessor>()
                .LogDebug(e, "Ignoring invalid endpoint {Uri} for exchange {ExchangeId}", value, exchange.ExchangeId);
            return;
        }

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
    protected override async Task DoStopAsync(CancellationToken cancellationToken)
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
    public override string ToString() => $"SendDynamic[{Uri}]";

    private static IEndpoint ResolveEndpoint(Exchange exchange, object? value)
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
