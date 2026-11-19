using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;
using Caravan.Language;
using Caravan.Model;

namespace Caravan.Support;

/// <summary>
/// Deep-copies route definitions (used by route templates, which give each route a copy of the template's route, and by
/// <see cref="RouteDefinition.Clone"/>). Works by reflection over instance fields, so new EIP definitions need no copy code:
/// <list type="bullet">
/// <item>definitions (<see cref="ProcessorDefinition"/>s, <see cref="RouteDefinition"/>s and the other classes of <c>Caravan.Model</c> such as
/// error handlers, redelivery policies and resequencer configurations) are copied field by field, each object once (shared references stay
/// shared within the copy);</item>
/// <item><see cref="List{T}"/>, <see cref="Dictionary{TKey, TValue}"/>, <see cref="HashSet{T}"/> and arrays are copied with their elements copied;</item>
/// <item>strings are passed through the optional text function (template parameters); Simple expressions and predicates are recreated when that
/// changes their text;</item>
/// <item>anything else (other expressions, delegates, types, beans, strategies) is shared.</item>
/// </list>
/// <see cref="ProcessorDefinition.Parent"/> and <see cref="ProcessorDefinition.Route"/> are mapped to the copies, or cleared when they point outside the copied graph.
/// </summary>
internal sealed class DefinitionCloner
{
    private static readonly Func<object, object> MemberwiseCloneFunc = typeof(object)
        .GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic)!
        .CreateDelegate<Func<object, object>>();

    private static readonly FieldInfo ParentField = BackingField(nameof(ProcessorDefinition.Parent));
    private static readonly FieldInfo RouteField = BackingField(nameof(ProcessorDefinition.Route));
    private static readonly ConcurrentDictionary<Type, FieldInfo[]> FieldsByType = new();

    private readonly Dictionary<object, object> _copies = new(ReferenceEqualityComparer.Instance);
    private readonly List<(object Copy, FieldInfo Field, object Original)> _backReferences = [];
    private readonly Func<string, string>? _text;

    private DefinitionCloner(Func<string, string>? text) => _text = text;

    /// <summary>Copies <paramref name="source"/> deeply.</summary>
    /// <param name="source">A route or node definition.</param>
    /// <param name="text">Transforms every string of the copy (and the text of Simple expressions), or null.</param>
    /// <typeparam name="T">The definition type.</typeparam>
    /// <returns>The copy.</returns>
    public static T Clone<T>(T source, Func<string, string>? text)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(source);
        var cloner = new DefinitionCloner(text);
        var copy = (T)cloner.Copy(source)!;
        cloner.FixBackReferences();
        return copy;
    }

    private static FieldInfo BackingField(string property) =>
        typeof(ProcessorDefinition).GetField($"<{property}>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException($"No backing field for ProcessorDefinition.{property}");

    private static bool IsDefinition(Type type) =>
        typeof(ProcessorDefinition).IsAssignableFrom(type) ||
        type == typeof(RouteDefinition) ||
        (type.IsClass && type.Assembly == typeof(ProcessorDefinition).Assembly && type.Namespace == typeof(ProcessorDefinition).Namespace);

    private static FieldInfo[] GetFields(Type type) => FieldsByType.GetOrAdd(type, static t =>
    {
        var fields = new List<FieldInfo>();
        for (var current = t; current is not null && current != typeof(object); current = current.BaseType)
        {
            fields.AddRange(current.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly));
        }

        return [.. fields];
    });

    private object? Copy(object? value)
    {
        switch (value)
        {
            case null:
                return null;
            case string text:
                return _text?.Invoke(text) ?? text;
            case Type or Delegate or MemberInfo:
                return value;
        }

        var type = value.GetType();
        if (type.IsValueType)
        {
            return value;
        }

        if (_copies.TryGetValue(value, out var existing))
        {
            return existing;
        }

        var copy = value switch
        {
            SimpleExpression expression => ChangedText(expression.Text) is { } text ? SimpleLanguage.Expression(text, expression.ResultType) : value,
            SimplePredicate predicate => ChangedText(predicate.Text) is { } text ? SimpleLanguage.Predicate(text) : value,
            SimpleBuilder builder => ChangedText(builder.Text) is { } text ? new SimpleBuilder(text, builder.ResultType) : value,
            LanguageExpression language => ChangedText(language.Text) is { } text ? language.WithText(text) : value,
            Array array => CopyArray(array),
            _ when IsDefinition(type) => CopyDefinition(value, type),
            _ when type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>) => CopyList((IList)value, type),
            _ when type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Dictionary<,>) => CopyDictionary((IDictionary)value, type),
            _ when type.IsGenericType && type.GetGenericTypeDefinition() == typeof(HashSet<>) => CopySet(value, type),
            _ => value,
        };

        _copies[value] = copy;
        return copy;
    }

    private string? ChangedText(string text) => _text?.Invoke(text) is { } changed && changed != text ? changed : null;

    private object CopyDefinition(object value, Type type)
    {
        var copy = MemberwiseCloneFunc(value);
        _copies[value] = copy;
        foreach (var field in GetFields(type))
        {
            var current = field.GetValue(copy);
            if (field == ParentField || field == RouteField)
            {
                if (current is not null)
                {
                    _backReferences.Add((copy, field, current));
                }

                continue;
            }

            var copied = Copy(current);
            if (!ReferenceEquals(current, copied))
            {
                field.SetValue(copy, copied);
            }
        }

        return copy;
    }

    private Array CopyArray(Array array)
    {
        var copy = (Array)array.Clone();
        _copies[array] = copy;
        if (!array.GetType().GetElementType()!.IsValueType)
        {
            for (var i = 0; i < copy.Length; i++)
            {
                copy.SetValue(Copy(array.GetValue(i)), i);
            }
        }

        return copy;
    }

    private IList CopyList(IList list, Type type)
    {
        var copy = (IList)Activator.CreateInstance(type, list.Count)!;
        _copies[list] = copy;
        foreach (var item in list)
        {
            copy.Add(Copy(item));
        }

        return copy;
    }

    private IDictionary CopyDictionary(IDictionary dictionary, Type type)
    {
        var comparer = type.GetProperty("Comparer")!.GetValue(dictionary);
        var copy = (IDictionary)Activator.CreateInstance(type, comparer)!;
        _copies[dictionary] = copy;
        foreach (DictionaryEntry entry in dictionary)
        {
            copy[Copy(entry.Key)!] = Copy(entry.Value);
        }

        return copy;
    }

    private object CopySet(object set, Type type)
    {
        var comparer = type.GetProperty("Comparer")!.GetValue(set);
        var copy = Activator.CreateInstance(type, comparer)!;
        _copies[set] = copy;
        var add = type.GetMethod("Add")!;
        foreach (var item in (IEnumerable)set)
        {
            add.Invoke(copy, [Copy(item)]);
        }

        return copy;
    }

    private void FixBackReferences()
    {
        foreach (var (copy, field, original) in _backReferences)
        {
            field.SetValue(copy, _copies.GetValueOrDefault(original));
        }
    }
}
