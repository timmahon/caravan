using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml;

namespace Caravan.Dsl.Yaml;

/// <summary>Parses Camel durations: milliseconds (<c>1000</c>), <c>1h2m3s4ms</c> (also <c>d</c>), ISO-8601 (<c>PT5S</c>) or <c>hh:mm:ss</c>.</summary>
internal static partial class DurationParser
{
    /// <summary>Parses <paramref name="text"/>; false when it is not a duration.</summary>
    public static bool TryParse(string text, out TimeSpan duration)
    {
        text = text.Trim();
        duration = default;
        if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var millis))
        {
            duration = TimeSpan.FromMilliseconds(millis);
            return true;
        }

        if (text.StartsWith('P') || text.StartsWith("-P", StringComparison.Ordinal))
        {
            try
            {
                duration = XmlConvert.ToTimeSpan(text);
                return true;
            }
            catch (FormatException)
            {
                return false;
            }
        }

        var match = Units().Match(text);
        if (match.Success && match.Length > 0)
        {
            duration = TimeSpan.FromDays(Value(match, "d")) + TimeSpan.FromHours(Value(match, "h")) + TimeSpan.FromMinutes(Value(match, "m"))
                + TimeSpan.FromSeconds(Value(match, "s")) + TimeSpan.FromMilliseconds(Value(match, "ms"));
            return true;
        }

        return text.Contains(':', StringComparison.Ordinal) && TimeSpan.TryParse(text, CultureInfo.InvariantCulture, out duration);
    }

    private static double Value(Match match, string group) =>
        match.Groups[group].Success ? double.Parse(match.Groups[group].Value, CultureInfo.InvariantCulture) : 0;

    [GeneratedRegex(@"^\s*(?:(?<d>\d+(?:\.\d+)?)\s*d(?:ays?)?\s*)?(?:(?<h>\d+(?:\.\d+)?)\s*h(?:ours?)?\s*)?(?:(?<m>\d+(?:\.\d+)?)\s*m(?:in(?:utes?)?)?(?!s)\s*)?(?:(?<s>\d+(?:\.\d+)?)\s*s(?:ec(?:onds?)?)?\s*)?(?:(?<ms>\d+)\s*ms\s*)?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Units();
}
