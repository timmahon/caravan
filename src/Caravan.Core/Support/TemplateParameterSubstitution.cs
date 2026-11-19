using System.Globalization;
using System.Text.RegularExpressions;

namespace Caravan.Support;

/// <summary>
/// Replaces the <c>{{placeholders}}</c> whose key is a route template parameter with the parameter's value (Camel's local properties: template
/// parameters take precedence over the context's properties). Other placeholders are left for the properties component, which resolves them when
/// endpoints are created or expressions evaluated. Handles defaults (<c>{{name:default}}</c>), optional placeholders (<c>{{?name}}</c>) and
/// nesting (<c>{{app.{{env}}.url}}</c>).
/// </summary>
internal sealed partial class TemplateParameterSubstitution
{
    private const int MaxRounds = 32;
    private readonly IReadOnlyDictionary<string, string> _values;

    /// <summary>Creates a substitution of <paramref name="parameters"/> (values converted to invariant strings; null values are skipped).</summary>
    public TemplateParameterSubstitution(IEnumerable<KeyValuePair<string, object?>> parameters)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in parameters)
        {
            if (value is not null)
            {
                values[key] = ToText(value);
            }
        }

        _values = values;
    }

    /// <summary>Converts a parameter value to the text substituted for it.</summary>
    public static string ToText(object value) => value switch
    {
        string text => text,
        bool flag => flag ? "true" : "false",
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty,
    };

    /// <summary>Substitutes the parameters in <paramref name="text"/>.</summary>
    public string Apply(string text)
    {
        if (_values.Count == 0 || !text.Contains("{{", StringComparison.Ordinal))
        {
            return text;
        }

        for (var round = 0; round < MaxRounds; round++)
        {
            var changed = false;
            var result = Placeholder().Replace(text, match =>
            {
                var content = match.Groups["content"].Value;
                var colon = content.IndexOf(':', StringComparison.Ordinal);
                var key = (colon < 0 ? content : content[..colon]).Trim();
                if (!_values.TryGetValue(key, out var value))
                {
                    return match.Value;
                }

                changed = true;
                return value;
            });

            if (!changed)
            {
                return result;
            }

            text = result;
        }

        throw new InvalidOperationException($"Circular reference detected while substituting template parameters in: {text}");
    }

    [GeneratedRegex(@"\{\{\??(?<content>[^{}]*)\}\}", RegexOptions.CultureInvariant)]
    private static partial Regex Placeholder();
}
