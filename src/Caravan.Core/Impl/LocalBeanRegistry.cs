using System.Collections.Concurrent;
using Caravan.Spi;

namespace Caravan.Impl;

/// <summary>
/// The beans local to one route (Camel's local bean repository), e.g. the template beans of a route created from a route template. While
/// such a route is created and while it processes an exchange, the context's <see cref="DefaultRegistry"/> looks names up here first, so
/// <c>.Bean("myBean")</c>, <c>${bean:myBean}</c>, <c>#myBean</c> options and <c>...Ref</c> lookups see the route's own instance; other routes do
/// not. The scope flows with the asynchronous calls of the route (an <see cref="AsyncLocal{T}"/>).
/// </summary>
/// <remarks>
/// Endpoints are cached by URI for the whole context, so a template refers to a local bean in a URI through the placeholder of the bean's name
/// (<c>bean:{{myBean}}</c>, <c>?strategy=#{{myBean}}</c>): it resolves to a name unique to the route (<see cref="RouteTemplateContext"/>), so each
/// route gets its own endpoint. Both names find the bean.
/// </remarks>
public sealed class LocalBeanRegistry : IRegistry
{
    private static readonly AsyncLocal<LocalBeanRegistry?> CurrentScope = new();
    private readonly ConcurrentDictionary<string, object> _beans = new(StringComparer.Ordinal);

    /// <summary>The local beans in scope on the current asynchronous flow, or null.</summary>
    public static LocalBeanRegistry? Current
    {
        get => CurrentScope.Value;
        internal set => CurrentScope.Value = value;
    }

    /// <summary>The names of the beans.</summary>
    public IReadOnlyCollection<string> Names => [.. _beans.Keys];

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
    public object? Lookup(string name) => _beans.GetValueOrDefault(name);

    /// <inheritdoc />
    public T? Lookup<T>(string name)
        where T : class => Lookup(name) as T;

    /// <inheritdoc />
    public IReadOnlyDictionary<string, T> FindByType<T>()
        where T : class => _beans.Where(kv => kv.Value is T).ToDictionary(kv => kv.Key, kv => (T)kv.Value, StringComparer.Ordinal);

    /// <inheritdoc />
    /// <remarks>A bean bound under several names (its local and unique name) counts once.</remarks>
    public T? FindSingleByType<T>()
        where T : class
    {
        var matches = _beans.Values.OfType<T>().Distinct<T>(ReferenceEqualityComparer.Instance).Take(2).ToList();
        return matches.Count == 1 ? matches[0] : null;
    }

    /// <summary>Makes <paramref name="registry"/> the scope of the current flow; dispose the result to restore the previous one.</summary>
    /// <param name="registry">The local beans, or null for none.</param>
    /// <returns>Restores the previous scope.</returns>
    internal static Scope Enter(LocalBeanRegistry? registry)
    {
        var previous = Current;
        Current = registry;
        return new Scope(previous);
    }

    /// <summary>Restores a previous scope.</summary>
    internal readonly struct Scope(LocalBeanRegistry? previous) : IDisposable
    {
        /// <inheritdoc />
        public void Dispose() => Current = previous;
    }
}
