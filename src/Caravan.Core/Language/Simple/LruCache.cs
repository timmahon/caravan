namespace Caravan.Language.Simple;

/// <summary>A thread-safe least-recently-used cache.</summary>
internal sealed class LruCache<TKey, TValue>(int capacity)
    where TKey : notnull
{
    private readonly Dictionary<TKey, LinkedListNode<(TKey Key, TValue Value)>> _map = [];
    private readonly LinkedList<(TKey Key, TValue Value)> _order = new();
    private readonly object _lock = new();

    /// <summary>The number of cached entries.</summary>
    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _map.Count;
            }
        }
    }

    /// <summary>The maximum number of entries.</summary>
    public int Capacity { get; set; } = capacity;

    /// <summary>Gets the cached value of <paramref name="key"/> or creates (outside the lock) and caches it.</summary>
    public TValue GetOrAdd(TKey key, Func<TKey, TValue> factory)
    {
        lock (_lock)
        {
            if (_map.TryGetValue(key, out var node))
            {
                _order.Remove(node);
                _order.AddFirst(node);
                return node.Value.Value;
            }
        }

        var value = factory(key);
        lock (_lock)
        {
            if (_map.TryGetValue(key, out var existing))
            {
                return existing.Value.Value;
            }

            _map[key] = _order.AddFirst((key, value));
            while (_map.Count > Math.Max(1, Capacity))
            {
                var last = _order.Last!;
                _order.RemoveLast();
                _map.Remove(last.Value.Key);
            }

            return value;
        }
    }

    /// <summary>Removes every entry.</summary>
    public void Clear()
    {
        lock (_lock)
        {
            _map.Clear();
            _order.Clear();
        }
    }
}
