using System.Text;
using YamlDotNet.RepresentationModel;

namespace Caravan.Dsl.Yaml;

/// <summary>Builds endpoint URIs from Camel's <c>uri</c> + <c>parameters</c> form.</summary>
internal static class EndpointUris
{
    /// <summary>The URI of <paramref name="options"/>' <c>uri</c> (or <paramref name="uriOption"/>) with its <c>parameters</c> appended.</summary>
    public static string Build(YamlObject options, string uriOption = "uri", string? prefix = null)
    {
        var uri = prefix + options.RequiredString(uriOption);
        var parameters = options.Map("parameters");
        if (parameters.Count == 0)
        {
            return uri;
        }

        var builder = new StringBuilder(uri);
        var separator = uri.Contains('?', StringComparison.Ordinal) ? '&' : '?';
        foreach (var (key, _, value) in parameters)
        {
            builder.Append(separator).Append(Encode(key)).Append('=').Append(Encode(Text(options, key, value)));
            separator = '&';
        }

        return builder.ToString();
    }

    private static string Text(YamlObject options, string key, YamlNode value) => value switch
    {
        YamlScalarNode scalar => options.Context.Text(scalar.Value ?? string.Empty),
        YamlSequenceNode sequence => string.Join(",", sequence.Children.Select(item => Text(options, key, item))),
        _ => throw options.Context.Error(value, $"Endpoint parameter '{key}' must be a scalar or a list"),
    };

    /// <summary>Escapes what would break the query string, keeping placeholders, <c>#bean</c> references and <c>RAW(...)</c> values readable.</summary>
    private static string Encode(string text) =>
        text.StartsWith("RAW(", StringComparison.Ordinal)
            ? text
            : text.Replace("%", "%25", StringComparison.Ordinal).Replace("&", "%26", StringComparison.Ordinal)
                .Replace("+", "%2B", StringComparison.Ordinal).Replace(" ", "%20", StringComparison.Ordinal);
}
