using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Caravan.Support;

/// <summary>Turns the value of a split or recipient list expression into the sequence of its parts.</summary>
internal static class SplitIterator
{
    private static readonly MethodInfo BoxAsyncMethod = typeof(SplitIterator).GetMethod(nameof(BoxAsync), BindingFlags.NonPublic | BindingFlags.Static)!;

    /// <summary>
    /// The parts of <paramref name="value"/>, lazily: nothing for null; the items of an <see cref="IAsyncEnumerable{T}"/> or an
    /// <see cref="IEnumerable"/> (except strings); a string split on <paramref name="delimiter"/> (parts trimmed, empty ones
    /// skipped) when a delimiter is given; otherwise the value itself.
    /// </summary>
    public static IAsyncEnumerable<object?> CreateAsync(object? value, string? delimiter)
    {
        switch (value)
        {
            case null:
                return EmptyAsync();
            case string text:
                return delimiter is null ? SingleAsync(text) : FromEnumerableAsync(SplitString(text, delimiter));
            case Message:
                return SingleAsync(value);
            case IAsyncEnumerable<object?> items:
                return items;
        }

        var asyncItemType = FindAsyncEnumerableItemType(value.GetType());
        if (asyncItemType is not null)
        {
            return (IAsyncEnumerable<object?>)BoxAsyncMethod.MakeGenericMethod(asyncItemType).Invoke(null, [value, CancellationToken.None])!;
        }

        return value is IEnumerable enumerable ? FromEnumerableAsync(enumerable) : SingleAsync(value);
    }

    /// <summary>The parts of <paramref name="value"/>, synchronously, with the rules of <see cref="CreateAsync"/> (asynchronous sequences are not supported).</summary>
    public static IEnumerable<object?> Create(object? value, string? delimiter)
    {
        switch (value)
        {
            case null:
                return [];
            case string text:
                return delimiter is null ? [text] : SplitString(text, delimiter);
            case Message:
                return [value];
            case IEnumerable enumerable:
                return enumerable.Cast<object?>();
            default:
                return [value];
        }
    }

    private static IEnumerable<object?> SplitString(string text, string delimiter) =>
        text.Split(delimiter, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    private static Type? FindAsyncEnumerableItemType(Type type)
    {
        foreach (var candidate in type.GetInterfaces().Prepend(type))
        {
            if (candidate.IsGenericType && candidate.GetGenericTypeDefinition() == typeof(IAsyncEnumerable<>))
            {
                return candidate.GetGenericArguments()[0];
            }
        }

        return null;
    }

    private static async IAsyncEnumerable<object?> BoxAsync<T>(IAsyncEnumerable<T> items, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var item in items.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            yield return item;
        }
    }

    private static async IAsyncEnumerable<object?> EmptyAsync()
    {
        await Task.CompletedTask.ConfigureAwait(false);
        yield break;
    }

    private static async IAsyncEnumerable<object?> SingleAsync(object? value)
    {
        await Task.CompletedTask.ConfigureAwait(false);
        yield return value;
    }

    private static async IAsyncEnumerable<object?> FromEnumerableAsync(IEnumerable items, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask.ConfigureAwait(false);
        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return item;
        }
    }
}
