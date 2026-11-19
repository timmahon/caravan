using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace Caravan.Support;

/// <summary>
/// Camel-style pattern matching: exact (case-insensitive), wildcard (<c>foo*</c>), or regular expression.
/// </summary>
public static class PatternHelper
{
    private static readonly ConcurrentDictionary<string, Regex?> Cache = new();

    public static bool Matches(string name, string pattern)
    {
        if (pattern == "*" || string.Equals(name, pattern, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (pattern.EndsWith('*') && pattern.IndexOf('*') == pattern.Length - 1 &&
            name.StartsWith(pattern[..^1], StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var regex = Cache.GetOrAdd(pattern, static p =>
        {
            try
            {
                return new Regex("^(?:" + p + ")$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            }
            catch (ArgumentException)
            {
                return null;
            }
        });

        return regex?.IsMatch(name) ?? false;
    }
}
