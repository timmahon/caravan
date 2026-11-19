using Caravan.Spi;

namespace Caravan.Support;

/// <summary>The default <see cref="IClaimCheckRepository"/> (Camel's <c>DefaultClaimCheckRepository</c>): a dictionary and a stack. Thread-safe.</summary>
public sealed class MemoryClaimCheckRepository : IClaimCheckRepository
{
    private readonly Dictionary<string, Exchange> _map = new(StringComparer.Ordinal);
    private readonly Stack<Exchange> _stack = new();
    private readonly Lock _lock = new();

    /// <inheritdoc />
    public bool Add(string key, Exchange exchange)
    {
        lock (_lock)
        {
            var added = !_map.ContainsKey(key);
            _map[key] = exchange;
            return added;
        }
    }

    /// <inheritdoc />
    public bool Contains(string key)
    {
        lock (_lock)
        {
            return _map.ContainsKey(key);
        }
    }

    /// <inheritdoc />
    public Exchange? Get(string key)
    {
        lock (_lock)
        {
            return _map.GetValueOrDefault(key);
        }
    }

    /// <inheritdoc />
    public Exchange? GetAndRemove(string key)
    {
        lock (_lock)
        {
            return _map.Remove(key, out var exchange) ? exchange : null;
        }
    }

    /// <inheritdoc />
    public void Push(Exchange exchange)
    {
        lock (_lock)
        {
            _stack.Push(exchange);
        }
    }

    /// <inheritdoc />
    public Exchange? Pop()
    {
        lock (_lock)
        {
            return _stack.TryPop(out var exchange) ? exchange : null;
        }
    }

    /// <inheritdoc />
    public void Clear()
    {
        lock (_lock)
        {
            _map.Clear();
            _stack.Clear();
        }
    }

    /// <inheritdoc />
    public override string ToString() => "MemoryClaimCheckRepository";
}
