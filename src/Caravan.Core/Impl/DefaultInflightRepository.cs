using Caravan.Spi;

namespace Caravan.Impl;

/// <summary>The default <see cref="IInflightRepository"/>: an in-memory map of exchanges to the routes processing them.</summary>
public sealed class DefaultInflightRepository : IInflightRepository
{
    private readonly object _lock = new();
    private readonly Dictionary<Exchange, List<string>> _exchanges = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<string, int> _routeSizes = new(StringComparer.Ordinal);
    private TaskCompletionSource _changed = NewSignal();

    /// <inheritdoc />
    public int Size
    {
        get
        {
            lock (_lock)
            {
                return _exchanges.Count;
            }
        }
    }

    /// <inheritdoc />
    public int GetSize(string routeId)
    {
        lock (_lock)
        {
            return _routeSizes.GetValueOrDefault(routeId);
        }
    }

    /// <inheritdoc />
    public void Add(Exchange exchange, string routeId)
    {
        lock (_lock)
        {
            if (!_exchanges.TryGetValue(exchange, out var routes))
            {
                _exchanges[exchange] = routes = [];
            }

            routes.Add(routeId);
            _routeSizes[routeId] = _routeSizes.GetValueOrDefault(routeId) + 1;
        }
    }

    /// <inheritdoc />
    public void Remove(Exchange exchange, string routeId)
    {
        TaskCompletionSource changed;
        lock (_lock)
        {
            if (_exchanges.TryGetValue(exchange, out var routes))
            {
                var index = routes.LastIndexOf(routeId);
                if (index >= 0)
                {
                    routes.RemoveAt(index);
                    var size = _routeSizes.GetValueOrDefault(routeId) - 1;
                    if (size > 0)
                    {
                        _routeSizes[routeId] = size;
                    }
                    else
                    {
                        _routeSizes.Remove(routeId);
                    }
                }

                if (routes.Count == 0)
                {
                    _exchanges.Remove(exchange);
                }
            }

            changed = _changed;
            _changed = NewSignal();
        }

        changed.TrySetResult();
    }

    /// <inheritdoc />
    public IReadOnlyList<InflightExchangeInfo> Browse(string? routeId = null, int limit = -1, bool sortByLongestDuration = false)
    {
        List<(Exchange Exchange, string RouteId)> snapshot;
        lock (_lock)
        {
            snapshot = _exchanges
                .Where(e => e.Value.Count > 0 && (routeId is null || e.Value.Contains(routeId)))
                .Select(e => (e.Key, e.Value[^1]))
                .ToList();
        }

        IEnumerable<InflightExchangeInfo> result = snapshot.Select(e =>
            new InflightExchangeInfo(e.Exchange, e.RouteId, e.Exchange.Context.TimeProvider.GetUtcNow() - e.Exchange.Created));
        if (sortByLongestDuration)
        {
            result = result.OrderByDescending(i => i.Duration);
        }

        if (limit >= 0)
        {
            result = result.Take(limit);
        }

        return result.ToList();
    }

    /// <summary>Completes when an exchange is next removed (used by the shutdown strategy to wait without polling).</summary>
    internal Task WaitForRemovalAsync()
    {
        lock (_lock)
        {
            return _changed.Task;
        }
    }

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
