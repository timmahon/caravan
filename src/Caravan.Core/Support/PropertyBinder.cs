using System.Collections.Concurrent;
using System.Reflection;

namespace Caravan.Support;

/// <summary>
/// Binds string options (usually endpoint URI parameters) onto writable properties, matching names
/// case-insensitively and converting values with the context's type converter.
/// A value of <c>#name</c> or <c>#bean:name</c> is looked up in the registry.
/// </summary>
public static class PropertyBinder
{
    private static readonly ConcurrentDictionary<Type, Dictionary<string, PropertyInfo>> Cache = new();

    /// <summary>Binds the options and returns the names of options that did not match a property.</summary>
    public static IReadOnlyList<string> Bind(ICaravanContext context, object target, IReadOnlyDictionary<string, string> options)
    {
        var properties = GetProperties(target.GetType());
        var unknown = new List<string>();
        foreach (var (key, raw) in options)
        {
            if (!properties.TryGetValue(key, out var property))
            {
                unknown.Add(key);
                continue;
            }

            SetProperty(context, target, property, raw);
        }

        return unknown;
    }

    /// <summary>Sets a single property by name. Returns false if there is no such writable property.</summary>
    public static bool TrySetProperty(ICaravanContext context, object target, string name, object? value)
    {
        if (!GetProperties(target.GetType()).TryGetValue(name, out var property))
        {
            return false;
        }

        SetProperty(context, target, property, value);
        return true;
    }

    /// <summary>Whether <paramref name="type"/> has a public settable property named <paramref name="name"/> (case-insensitive).</summary>
    public static bool HasProperty(Type type, string name) => GetProperties(type).ContainsKey(name);

    private static void SetProperty(ICaravanContext context, object target, PropertyInfo property, object? value)
    {
        if (value is string s && s.StartsWith('#'))
        {
            var name = s.StartsWith("#bean:", StringComparison.Ordinal) ? s[6..] : s[1..];
            value = context.Registry.Lookup(name) ?? throw new NoSuchBeanException(name);
        }
        else if (value is string placeholder)
        {
            value = context.ResolvePropertyPlaceholders(placeholder);
        }

        try
        {
            var converted = context.TypeConverter.Convert(property.PropertyType, null, value);
            property.SetValue(target, converted);
        }
        catch (Exception e) when (e is not CaravanException)
        {
            throw new ArgumentException(
                $"Cannot set option '{property.Name}' on {target.GetType().Name} to value '{value}'", e);
        }
        catch (TypeConversion.NoTypeConversionAvailableException e)
        {
            throw new ArgumentException(
                $"Cannot set option '{property.Name}' on {target.GetType().Name} to value '{value}'", e);
        }
    }

    private static Dictionary<string, PropertyInfo> GetProperties(Type type) =>
        Cache.GetOrAdd(type, t => t
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanWrite && p.SetMethod is { IsPublic: true } && p.GetIndexParameters().Length == 0)
            .GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase));
}
