using System.Globalization;
using System.Text.RegularExpressions;

namespace Caravan.Dsl.Yaml;

/// <summary>Substitutes route template parameters (<c>{{name}}</c>, <c>{{name:default}}</c>, <c>{{?name}}</c>) into the texts of template beans.</summary>
internal static partial class TemplateText
{
    /// <summary>
    /// <paramref name="text"/> with the placeholders naming a parameter replaced by its value; other placeholders are left to the
    /// properties component.
    /// </summary>
    public static string Substitute(string text, IDictionary<string, object?> parameters) =>
        !text.Contains("{{", StringComparison.Ordinal)
            ? text
            : Placeholder().Replace(text, match =>
            {
                var key = match.Groups["key"].Value.Trim();
                return parameters.TryGetValue(key, out var value) && value is not null
                    ? value is IFormattable formattable ? formattable.ToString(null, CultureInfo.InvariantCulture) : value.ToString() ?? string.Empty
                    : match.Value;
            });

    [GeneratedRegex(@"\{\{\??(?<key>[^{}:]+)(?::[^{}]*)?\}\}", RegexOptions.CultureInvariant)]
    private static partial Regex Placeholder();
}
