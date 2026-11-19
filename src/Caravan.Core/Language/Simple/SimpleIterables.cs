using System.Collections;

namespace Caravan.Language.Simple;

/// <summary>Iteration helpers of the <c>collate</c>, <c>skip</c> and <c>join</c> functions.</summary>
internal static class SimpleIterables
{
    /// <summary>
    /// The items of <paramref name="value"/> like Camel's <c>ObjectHelper.createIterator</c>: a collection's elements, a string's
    /// comma-separated parts, nothing for null, else the value itself.
    /// </summary>
    public static IEnumerable<object?> Items(object? value) => value switch
    {
        null => [],
        string s => s.Length == 0 ? [] : s.Split(',').Select(p => (object?)p.Trim()),
        IEnumerable e => e.Cast<object?>(),
        var single => [single],
    };

    /// <summary>Groups <paramref name="items"/> into lists of <paramref name="size"/> (lazily).</summary>
    public static IEnumerable<List<object?>> Collate(IEnumerable<object?> items, int size)
    {
        var group = new List<object?>(size);
        foreach (var item in items)
        {
            group.Add(item);
            if (group.Count == size)
            {
                yield return group;
                group = new List<object?>(size);
            }
        }

        if (group.Count > 0)
        {
            yield return group;
        }
    }
}
