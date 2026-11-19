using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;

namespace Caravan.Language.Simple;

/// <summary>
/// Resolves type names used by Simple (<c>bodyAs(String)</c>, <c>is 'Integer'</c>, <c>type:...</c>): C# keywords (<c>int</c>), Java
/// names familiar to Camel users (<c>String</c>, <c>Integer</c>, <c>java.lang.Long</c>, <c>BigDecimal</c>, <c>List</c>, <c>Map</c>),
/// assembly-qualified and full names, then a unique simple name among the types of the loaded assemblies; <c>[]</c> makes an array.
/// </summary>
internal static class TypeNameResolver
{
    private static readonly Dictionary<string, Type> Aliases = new(StringComparer.Ordinal)
    {
        ["string"] = typeof(string),
        ["String"] = typeof(string),
        ["int"] = typeof(int),
        ["Integer"] = typeof(int),
        ["long"] = typeof(long),
        ["Long"] = typeof(long),
        ["double"] = typeof(double),
        ["Double"] = typeof(double),
        ["float"] = typeof(float),
        ["Float"] = typeof(float),
        ["bool"] = typeof(bool),
        ["boolean"] = typeof(bool),
        ["Boolean"] = typeof(bool),
        ["short"] = typeof(short),
        ["Short"] = typeof(short),
        ["byte"] = typeof(byte),
        ["Byte"] = typeof(byte),
        ["sbyte"] = typeof(sbyte),
        ["char"] = typeof(char),
        ["Character"] = typeof(char),
        ["decimal"] = typeof(decimal),
        ["BigDecimal"] = typeof(decimal),
        ["BigInteger"] = typeof(System.Numerics.BigInteger),
        ["object"] = typeof(object),
        ["Object"] = typeof(object),
        ["uint"] = typeof(uint),
        ["ulong"] = typeof(ulong),
        ["ushort"] = typeof(ushort),
        ["Date"] = typeof(DateTimeOffset),
        ["List"] = typeof(IList),
        ["Collection"] = typeof(ICollection),
        ["Iterable"] = typeof(IEnumerable),
        ["Map"] = typeof(IDictionary),
    };

    private static readonly ConcurrentDictionary<string, Type?> Cache = new(StringComparer.Ordinal);

    /// <summary>The type named <paramref name="name"/>, or null when there is none (or the simple name is ambiguous).</summary>
    public static Type? Resolve(string name)
    {
        name = name.Trim().Trim('\'', '"').Trim();
        if (name.Length == 0)
        {
            return null;
        }

        if (Cache.TryGetValue(name, out var cached))
        {
            return cached;
        }

        var type = DoResolve(name);
        if (type is not null)
        {
            Cache[name] = type;
        }

        return type;
    }

    private static Type? DoResolve(string name)
    {
        if (name.EndsWith("[]", StringComparison.Ordinal))
        {
            return Resolve(name[..^2])?.MakeArrayType();
        }

        if (name.StartsWith("java.lang.", StringComparison.Ordinal) || name.StartsWith("java.util.", StringComparison.Ordinal) ||
            name.StartsWith("java.math.", StringComparison.Ordinal))
        {
            name = name[(name.LastIndexOf('.') + 1)..];
        }

        if (Aliases.TryGetValue(name, out var alias))
        {
            return alias;
        }

        if (Type.GetType(name, throwOnError: false) is { } direct)
        {
            return direct;
        }

        var assemblies = AppDomain.CurrentDomain.GetAssemblies();
        foreach (var assembly in assemblies)
        {
            if (assembly.GetType(name, throwOnError: false) is { } found)
            {
                return found;
            }
        }

        if (name.Contains('.', StringComparison.Ordinal))
        {
            return null;
        }

        Type? match = null;
        foreach (var assembly in assemblies.Where(a => !a.IsDynamic))
        {
            foreach (var type in SafeGetTypes(assembly))
            {
                if (type.Name == name && type.IsPublic)
                {
                    if (match is not null && match != type)
                    {
                        return null;
                    }

                    match = type;
                }
            }
        }

        return match;
    }

    private static IEnumerable<Type> SafeGetTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetExportedTypes();
        }
        catch (Exception)
        {
            return [];
        }
    }
}
