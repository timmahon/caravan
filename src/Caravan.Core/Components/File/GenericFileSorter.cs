using Caravan.Language;
using Caravan.Support;

namespace Caravan.Components.File;

/// <summary>Builds the comparer of the <c>sortBy</c> option (Camel's <c>GenericFileDefaultSorter.sortByFileLanguage</c>).</summary>
internal static class GenericFileSorter
{
    /// <summary>Parses <c>[reverse:][ignoreCase:]expression</c> groups separated by <c>;</c>.</summary>
    public static IComparer<Exchange> Create(string sortBy)
    {
        var groups = sortBy.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (groups.Length == 0)
        {
            throw new ArgumentException("sortBy must not be empty");
        }

        IComparer<Exchange>? answer = null;
        for (var i = groups.Length - 1; i >= 0; i--)
        {
            answer = CreateGroup(groups[i], answer);
        }

        return answer!;
    }

    private static IComparer<Exchange> CreateGroup(string group, IComparer<Exchange>? next)
    {
        var reverse = false;
        var ignoreCase = false;
        while (true)
        {
            if (group.StartsWith("reverse:", StringComparison.Ordinal))
            {
                reverse = true;
                group = group["reverse:".Length..];
            }
            else if (group.StartsWith("ignoreCase:", StringComparison.Ordinal))
            {
                ignoreCase = true;
                group = group["ignoreCase:".Length..];
            }
            else
            {
                break;
            }
        }

        var text = group.Contains("${", StringComparison.Ordinal) ? group : "${" + group + "}";
        var expression = SimpleLanguage.Expression(text);
        return Comparer<Exchange>.Create((a, b) =>
        {
            var left = expression.Evaluate(a);
            var right = expression.Evaluate(b);
            var result = ignoreCase && left is string ls && right is string rs
                ? string.Compare(ls, rs, StringComparison.OrdinalIgnoreCase)
                : Compare(a, left, right);
            if (reverse)
            {
                result = -result;
            }

            return result == 0 && next is not null ? next.Compare(a, b) : result;
        });
    }

    private static int Compare(Exchange exchange, object? left, object? right) => (left, right) switch
    {
        (null, null) => 0,
        (null, _) => -1,
        (_, null) => 1,
        (string ls, string rs) => string.CompareOrdinal(ls, rs),
        _ => ObjectHelper.TypeCoerceCompare(exchange, left, right),
    };
}
