using System.Collections.Concurrent;
using Caravan.Spi;

namespace Caravan.Impl;

/// <summary>A thread-safe in-memory <see cref="IVariableRepository"/> (Camel's <c>GlobalVariableRepository</c>/<c>RouteVariableRepository</c>).</summary>
/// <param name="id">The id of the repository (the prefix of its variable names).</param>
public sealed class InMemoryVariableRepository(string id) : IVariableRepository
{
    private readonly ConcurrentDictionary<string, object?> _variables = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public string Id { get; } = id;

    /// <inheritdoc />
    public IReadOnlyDictionary<string, object?> Variables => new Dictionary<string, object?>(_variables, StringComparer.Ordinal);

    /// <inheritdoc />
    public object? GetVariable(string name) => _variables.TryGetValue(name, out var value) ? value : null;

    /// <inheritdoc />
    public void SetVariable(string name, object? value)
    {
        if (value is null)
        {
            _variables.TryRemove(name, out _);
        }
        else
        {
            _variables[name] = value;
        }
    }

    /// <inheritdoc />
    public object? RemoveVariable(string name) => _variables.TryRemove(name, out var value) ? value : null;

    /// <inheritdoc />
    public void Clear() => _variables.Clear();

    /// <inheritdoc />
    public override string ToString() => $"VariableRepository[{Id}]";
}
