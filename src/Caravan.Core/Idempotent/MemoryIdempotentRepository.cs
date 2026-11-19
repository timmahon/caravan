using Caravan.Spi;

namespace Caravan.Idempotent;

/// <summary>
/// An in-memory <see cref="IIdempotentRepository"/> (Camel's <c>MemoryIdempotentRepository</c>) holding at most <see cref="MaxCacheSize"/>
/// keys: when full, adding a key evicts the least recently added one. Keys are confirmed as soon as they are added. Thread-safe.
/// </summary>
public sealed class MemoryIdempotentRepository : IIdempotentRepository
{
    /// <summary>The default maximum number of keys (as Camel).</summary>
    public const int DefaultMaxCacheSize = 1000;

    private readonly Dictionary<string, LinkedListNode<string>> _keys = new(StringComparer.Ordinal);
    private readonly LinkedList<string> _order = new();
    private readonly Lock _lock = new();

    /// <summary>Creates the repository.</summary>
    /// <param name="maxCacheSize">The maximum number of keys kept (must be positive).</param>
    public MemoryIdempotentRepository(int maxCacheSize = DefaultMaxCacheSize)
    {
        if (maxCacheSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxCacheSize), "The cache size must be positive");
        }

        MaxCacheSize = maxCacheSize;
    }

    /// <summary>The maximum number of keys kept.</summary>
    public int MaxCacheSize { get; }

    /// <summary>The number of keys held.</summary>
    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _keys.Count;
            }
        }
    }

    /// <inheritdoc />
    public ValueTask<bool> AddAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        lock (_lock)
        {
            if (_keys.ContainsKey(key))
            {
                return ValueTask.FromResult(false);
            }

            _keys[key] = _order.AddLast(key);
            if (_keys.Count > MaxCacheSize)
            {
                var eldest = _order.First!;
                _order.RemoveFirst();
                _keys.Remove(eldest.Value);
            }

            return ValueTask.FromResult(true);
        }
    }

    /// <inheritdoc />
    public ValueTask<bool> ContainsAsync(string key, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            return ValueTask.FromResult(_keys.ContainsKey(key));
        }
    }

    /// <inheritdoc />
    public ValueTask<bool> RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            if (!_keys.Remove(key, out var node))
            {
                return ValueTask.FromResult(false);
            }

            _order.Remove(node);
            return ValueTask.FromResult(true);
        }
    }

    /// <inheritdoc />
    public ValueTask<bool> ConfirmAsync(string key, CancellationToken cancellationToken = default) => ValueTask.FromResult(true);

    /// <inheritdoc />
    public ValueTask ClearAsync(CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            _keys.Clear();
            _order.Clear();
        }

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public override string ToString() => $"MemoryIdempotentRepository[{Count}/{MaxCacheSize}]";
}
