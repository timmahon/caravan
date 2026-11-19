using System.Collections.Concurrent;
using System.Reflection;

namespace Caravan.Components.Bean;

/// <summary>
/// The public methods of a bean type and the method selection for each method specification (Camel's <c>BeanInfo</c>), cached per type.
/// </summary>
internal sealed class BeanInfo
{
    private static readonly ConcurrentDictionary<Type, BeanInfo> Cache = new();

    private static readonly HashSet<string> ExcludedNames = new(StringComparer.Ordinal)
    {
        nameof(Equals), nameof(GetHashCode), nameof(ToString), nameof(GetType), "Dispose", "DisposeAsync", "Deconstruct", "Finalize",
    };

    private readonly MethodInfo[] _publicMethods;
    private readonly ConcurrentDictionary<(string? Spec, bool StaticOnly), BeanMethodSelector> _selectors = new();
    private readonly ConcurrentDictionary<(MethodInfo Method, string? Spec), BeanMethodInvoker> _invokers = new();

    private BeanInfo(Type type)
    {
        Type = type;
        _publicMethods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
            .Where(m => !m.ContainsGenericParameters && !m.GetParameters().Any(p => p.ParameterType.IsByRef))
            .ToArray();
        Operations = _publicMethods
            .Where(m => !m.IsSpecialName && m.DeclaringType != typeof(object) && !m.Name.StartsWith('<') && !ExcludedNames.Contains(m.Name))
            .ToArray();
    }

    public Type Type { get; }

    /// <summary>The methods considered when no method name is given.</summary>
    public IReadOnlyList<MethodInfo> Operations { get; }

    public static BeanInfo For(Type type) => Cache.GetOrAdd(type, t => new BeanInfo(t));

    /// <summary>The public methods named <paramref name="name"/> (exact case first, then ignoring case).</summary>
    public MethodInfo[] MethodsNamed(string name)
    {
        var exact = _publicMethods.Where(m => m.Name.Equals(name, StringComparison.Ordinal)).ToArray();
        return exact.Length > 0 ? exact : _publicMethods.Where(m => m.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).ToArray();
    }

    /// <summary>The selector for a method specification (null: no method name).</summary>
    /// <exception cref="MethodNotFoundException">No method matches the specification.</exception>
    /// <exception cref="ArgumentException">The specification is invalid.</exception>
    public BeanMethodSelector GetSelector(string? methodSpec, bool staticOnly) =>
        _selectors.GetOrAdd((methodSpec, staticOnly), key => new BeanMethodSelector(this, key.Spec is null ? null : BeanMethodSpec.Parse(key.Spec), key.StaticOnly));

    public BeanMethodInvoker GetInvoker(MethodInfo method, BeanMethodSpec? spec) =>
        _invokers.GetOrAdd(
            (method, spec is { Parameters: not null, IsTypeSelection: false } ? spec.Text : null),
            _ => new BeanMethodInvoker(method, spec is { IsTypeSelection: false } ? spec.Parameters : null));

    public override string ToString() => $"BeanInfo[{Type.FullName}]";
}
