using System.Collections.Concurrent;
using Caravan.Spi;

namespace Caravan.Impl;

/// <summary>
/// In-memory registry. When an <see cref="IServiceProvider"/> is supplied, typed lookups fall back to it,
/// so beans registered in dependency injection are visible to routes.
/// </summary>
public sealed class DefaultRegistry(IServiceProvider? services = null) : IRegistry
{
    private readonly ConcurrentDictionary<string, object> _beans = new(StringComparer.Ordinal);

    /// <summary>The service provider used as a fallback for lookups, if any.</summary>
    public IServiceProvider? Services { get; } = services;

    /// <inheritdoc />
    public void Bind(string name, object bean)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(bean);
        _beans[name] = bean;
    }

    /// <inheritdoc />
    public bool Unbind(string name) => _beans.TryRemove(name, out _);

    /// <inheritdoc />
    public object? Lookup(string name) => _beans.TryGetValue(name, out var bean) ? bean : null;

    /// <inheritdoc />
    public T? Lookup<T>(string name)
        where T : class => Lookup(name) as T;

    /// <inheritdoc />
    public IReadOnlyDictionary<string, T> FindByType<T>()
        where T : class
    {
        var result = _beans.Where(kv => kv.Value is T).ToDictionary(kv => kv.Key, kv => (T)kv.Value);
        if (result.Count == 0 && Services?.GetService(typeof(IEnumerable<T>)) is IEnumerable<T> fromServices)
        {
            var i = 0;
            foreach (var bean in fromServices)
            {
                result[$"{typeof(T).Name}-{i++}"] = bean;
            }
        }

        return result;
    }

    /// <inheritdoc />
    public T? FindSingleByType<T>()
        where T : class
    {
        var matches = _beans.Values.OfType<T>().Take(2).ToList();
        if (matches.Count == 1)
        {
            return matches[0];
        }

        return matches.Count == 0 ? Services?.GetService(typeof(T)) as T : null;
    }
}
