using System.Collections;
using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;

namespace Caravan.Language.Simple;

/// <summary>Reflection used by OGNL steps, with cached member lookups.</summary>
internal static class MemberInvoker
{
    private static readonly ConcurrentDictionary<(Type Type, string Name), Func<object, object?>?> Getters = new();
    private static readonly ConcurrentDictionary<(Type Type, string Name, int Arity), MethodInfo[]> Methods = new();

    /// <summary>The property or field <paramref name="name"/> of <paramref name="target"/>, or the dictionary entry with that key.</summary>
    public static object? GetMember(object target, string name, string text)
    {
        var getter = Getters.GetOrAdd((target.GetType(), name), key => FindGetter(key.Type, key.Name));
        if (getter is not null)
        {
            return Get(getter, target, name, text);
        }

        if (TryGetDictionaryValue(target, name, null, out var value))
        {
            return value;
        }

        throw new ExpressionEvaluationException(text, $"Failed to evaluate ${{{text}}}: no property or field '{name}' on {target.GetType().FullName}");
    }

    /// <summary>Calls the public instance method <paramref name="name"/> with <paramref name="arguments"/> converted to its parameter types.</summary>
    public static object? Invoke(object target, string name, object?[] arguments, Exchange exchange, string text)
    {
        var type = target.GetType();
        var candidates = Methods.GetOrAdd((type, name, arguments.Length), key => FindMethods(key.Type, key.Name, key.Arity));
        foreach (var method in candidates)
        {
            if (TryConvertArguments(method.GetParameters(), arguments, exchange, out var converted))
            {
                try
                {
                    return method.Invoke(target, converted);
                }
                catch (TargetInvocationException e) when (e.InnerException is not null)
                {
                    throw new ExpressionEvaluationException(text, $"Failed to invoke method {name} on {type.FullName} in ${{{text}}}: {e.InnerException.Message}", e.InnerException);
                }
            }
        }

        if (arguments.Length == 0)
        {
            // Java style: getName() / isActive() / size() / length() read the matching property.
            foreach (var property in PropertyNamesFor(name))
            {
                var getter = Getters.GetOrAdd((type, property), key => FindGetter(key.Type, key.Name));
                if (getter is not null)
                {
                    return Get(getter, target, name, text);
                }
            }
        }

        var reason = candidates.Length > 0 ? "the arguments do not match" : "no such method";
        throw new ExpressionEvaluationException(text, $"Failed to invoke method {name} with {arguments.Length} argument(s) on {type.FullName} in ${{{text}}}: {reason}");
    }

    /// <summary>The element at <paramref name="key"/> of a list/array (<c>last</c>, <c>last-N</c> supported) or the dictionary entry with that key.</summary>
    public static object? Index(object target, string key, bool nullSafe, Exchange exchange, string text)
    {
        if (TryGetDictionaryValue(target, key, exchange, out var entry))
        {
            return entry;
        }

        IList? list = target as IList;
        if (list is null && target is IEnumerable enumerable)
        {
            list = enumerable.Cast<object?>().ToList();
        }

        if (list is not null)
        {
            var index = ParseIndex(key, list.Count, text);
            if (index >= 0 && index < list.Count)
            {
                return list[index];
            }

            return nullSafe
                ? null
                : throw new ExpressionEvaluationException(text, $"Failed to evaluate ${{{text}}}: index {key} is out of bounds (size {list.Count})");
        }

        var indexer = target.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .FirstOrDefault(p => p.GetIndexParameters().Length == 1);
        if (indexer is not null)
        {
            var parameterType = indexer.GetIndexParameters()[0].ParameterType;
            if (exchange.Context.TypeConverter.TryConvert(parameterType, exchange, key, out var converted))
            {
                try
                {
                    return indexer.GetValue(target, [converted]);
                }
                catch (TargetInvocationException e) when (e.InnerException is not null)
                {
                    return nullSafe ? null : throw new ExpressionEvaluationException(text, $"Failed to evaluate ${{{text}}}: {e.InnerException.Message}", e.InnerException);
                }
            }
        }

        throw new ExpressionEvaluationException(text, $"Failed to evaluate ${{{text}}}: {target.GetType().FullName} cannot be indexed");
    }

    private static object? Get(Func<object, object?> getter, object target, string name, string text)
    {
        try
        {
            return getter(target);
        }
        catch (TargetInvocationException e) when (e.InnerException is not null)
        {
            throw new ExpressionEvaluationException(text, $"Failed to read {name} on {target.GetType().FullName} in ${{{text}}}: {e.InnerException.Message}", e.InnerException);
        }
    }

    private static int ParseIndex(string key, int count, string text)
    {
        if (key == "last")
        {
            return count - 1;
        }

        if (key.StartsWith("last-", StringComparison.Ordinal) && int.TryParse(key[5..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var back))
        {
            return count - 1 - back;
        }

        return int.TryParse(key, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index)
            ? index
            : throw new ExpressionEvaluationException(text, $"Failed to evaluate ${{{text}}}: '{key}' is not a valid list index");
    }

    private static bool TryGetDictionaryValue(object target, string key, Exchange? exchange, out object? value)
    {
        value = null;
        switch (target)
        {
            case IDictionary<string, object?> generic:
                generic.TryGetValue(key, out value);
                return true;
            case IReadOnlyDictionary<string, object?> readOnly:
                readOnly.TryGetValue(key, out value);
                return true;
            case IDictionary dictionary:
                var keyType = DictionaryKeyType(dictionary.GetType());
                object convertedKey = key;
                if (keyType is not null && keyType != typeof(string) && exchange is not null &&
                    exchange.Context.TypeConverter.TryConvert(keyType, exchange, key, out var k) && k is not null)
                {
                    convertedKey = k;
                }
                else if (keyType is not null && !keyType.IsInstanceOfType(key))
                {
                    return true;
                }

                value = dictionary.Contains(convertedKey) ? dictionary[convertedKey] : null;
                return true;
            default:
                return false;
        }
    }

    private static Type? DictionaryKeyType(Type type) =>
        type.GetInterfaces()
            .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IDictionary<,>))
            ?.GetGenericArguments()[0];

    private static IEnumerable<string> PropertyNamesFor(string method)
    {
        yield return method;
        if (method.Length > 3 && method.StartsWith("get", StringComparison.Ordinal) && char.IsUpper(method[3]))
        {
            yield return method[3..];
        }

        if (method.Length > 2 && method.StartsWith("is", StringComparison.Ordinal) && char.IsUpper(method[2]))
        {
            yield return method[2..];
        }
    }

    private static Func<object, object?>? FindGetter(Type type, string name)
    {
        const BindingFlags Flags = BindingFlags.Public | BindingFlags.Instance;
        foreach (var comparison in (StringComparison[])[StringComparison.Ordinal, StringComparison.OrdinalIgnoreCase])
        {
            var property = type.GetProperties(Flags).FirstOrDefault(p => p.GetIndexParameters().Length == 0 && string.Equals(p.Name, name, comparison));
            if (property is { CanRead: true })
            {
                return property.GetValue;
            }

            var field = type.GetFields(Flags).FirstOrDefault(f => string.Equals(f.Name, name, comparison));
            if (field is not null)
            {
                return field.GetValue;
            }
        }

        // Java-isms: size and length mean Count or Length.
        if (name is "size" or "length")
        {
            return FindGetter(type, "Count") ?? (name == "length" || type.IsArray ? FindGetter(type, "Length") : null);
        }

        return null;
    }

    private static MethodInfo[] FindMethods(Type type, string name, int arity)
    {
        var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => !m.IsGenericMethodDefinition && m.GetParameters().Length == arity)
            .ToList();
        var exact = methods.Where(m => m.Name == name).ToArray();
        return exact.Length > 0 ? exact : methods.Where(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase)).ToArray();
    }

    private static bool TryConvertArguments(ParameterInfo[] parameters, object?[] arguments, Exchange exchange, out object?[] converted)
    {
        converted = new object?[arguments.Length];
        for (var i = 0; i < arguments.Length; i++)
        {
            var type = parameters[i].ParameterType;
            var argument = arguments[i];
            if (argument is null)
            {
                if (type.IsValueType && Nullable.GetUnderlyingType(type) is null)
                {
                    return false;
                }

                continue;
            }

            if (type.IsInstanceOfType(argument))
            {
                converted[i] = argument;
            }
            else if (exchange.Context.TypeConverter.TryConvert(type, exchange, argument, out var value))
            {
                converted[i] = value;
            }
            else
            {
                return false;
            }
        }

        return true;
    }
}
