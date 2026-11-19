using System.Text;

namespace Caravan.Dsl.Yaml;

/// <summary>Camel accepts kebab-case names (<c>parallel-processing</c>, <c>set-header</c>) as well as camelCase ones.</summary>
internal static class NameConverter
{
    /// <summary>The camelCase form of <paramref name="name"/> (<c>to-d</c> → <c>toD</c>).</summary>
    public static string ToCamelCase(string name)
    {
        if (!name.Contains('-', StringComparison.Ordinal))
        {
            return name;
        }

        var builder = new StringBuilder(name.Length);
        var upper = false;
        foreach (var c in name)
        {
            if (c == '-')
            {
                upper = builder.Length > 0;
                continue;
            }

            builder.Append(upper ? char.ToUpperInvariant(c) : c);
            upper = false;
        }

        return builder.ToString();
    }

    /// <summary>The known name closest to <paramref name="name"/> (for "did you mean" hints), or null when none is close.</summary>
    public static string? Closest(string name, IEnumerable<string> candidates)
    {
        string? best = null;
        var bestDistance = int.MaxValue;
        foreach (var candidate in candidates)
        {
            var distance = Distance(name.ToLowerInvariant(), candidate.ToLowerInvariant());
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = candidate;
            }
        }

        return bestDistance <= Math.Max(2, name.Length / 3) ? best : null;
    }

    private static int Distance(string a, string b)
    {
        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++)
        {
            previous[j] = j;
        }

        for (var i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + cost);
            }

            (previous, current) = (current, previous);
        }

        return previous[b.Length];
    }
}
