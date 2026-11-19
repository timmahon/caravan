using System.Text;

namespace Caravan.Support;

/// <summary>
/// A parsed endpoint URI: <c>scheme:path?key=value&amp;key2=value2</c>. The <c>scheme://path</c> form is also accepted.
/// </summary>
public sealed class EndpointUri
{
    private EndpointUri(string scheme, string path, IReadOnlyDictionary<string, string> parameters)
    {
        Scheme = scheme;
        Path = path;
        Parameters = parameters;
    }

    /// <summary>The URI scheme (component name).</summary>
    public string Scheme { get; }

    /// <summary>The part between the scheme and the query, without leading slashes.</summary>
    public string Path { get; }

    /// <summary>The decoded query parameters (<c>RAW(...)</c> values kept verbatim).</summary>
    public IReadOnlyDictionary<string, string> Parameters { get; }

    /// <summary>Parses <c>scheme:path?query</c> (or <c>scheme://path?query</c>).</summary>
    /// <exception cref="ResolveEndpointFailedException">The URI is invalid.</exception>
    public static EndpointUri Parse(string uri)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(uri);
        uri = uri.Trim();

        var colon = uri.IndexOf(':');
        if (colon <= 0)
        {
            throw new ResolveEndpointFailedException(uri, "URI must start with a scheme, for example 'direct:start'");
        }

        var scheme = uri[..colon];
        var rest = uri[(colon + 1)..];
        if (rest.StartsWith("//", StringComparison.Ordinal))
        {
            rest = rest[2..];
        }

        var query = string.Empty;
        var q = rest.IndexOf('?');
        if (q >= 0)
        {
            query = rest[(q + 1)..];
            rest = rest[..q];
        }

        return new EndpointUri(scheme, rest, ParseQuery(query));
    }

    /// <summary>Parses a query string into a dictionary, decoding values except <c>RAW(...)</c> ones.</summary>
    public static Dictionary<string, string> ParseQuery(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(query))
        {
            return result;
        }

        foreach (var pair in SplitQuery(query))
        {
            var eq = pair.IndexOf('=');
            var key = Uri.UnescapeDataString(eq < 0 ? pair : pair[..eq]);
            var value = eq < 0 ? string.Empty : DecodeValue(pair[(eq + 1)..]);
            result[key] = value;
        }

        return result;
    }

    /// <summary>
    /// Normalizes a URI so that equivalent URIs map to the same endpoint:
    /// <c>scheme://path</c> becomes <c>scheme:path</c> and parameters are sorted.
    /// </summary>
    public static string Normalize(string uri)
    {
        var parsed = Parse(uri);
        var sb = new StringBuilder(parsed.Scheme).Append(':').Append(parsed.Path);
        if (parsed.Parameters.Count > 0)
        {
            sb.Append('?');
            sb.AppendJoin('&', parsed.Parameters
                .OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase)
                .Select(p => p.Key + "=" + p.Value));
        }

        return sb.ToString();
    }

    /// <summary>Splits on '&amp;' but keeps <c>RAW(...)</c> values intact.</summary>
    private static IEnumerable<string> SplitQuery(string query)
    {
        var start = 0;
        var i = 0;
        while (i < query.Length)
        {
            if (string.CompareOrdinal(query, i, "=RAW(", 0, 5) == 0)
            {
                var close = query.IndexOf(')', i + 5);
                if (close > 0)
                {
                    // RAW values end at the last ')' before the next '&' that follows a ')'.
                    var next = close;
                    while (next + 1 < query.Length && query[next + 1] != '&')
                    {
                        var more = query.IndexOf(')', next + 1);
                        if (more < 0)
                        {
                            break;
                        }

                        next = more;
                    }

                    i = next + 1;
                    continue;
                }
            }

            if (query[i] == '&')
            {
                if (i > start)
                {
                    yield return query[start..i];
                }

                start = i + 1;
            }

            i++;
        }

        if (start < query.Length)
        {
            yield return query[start..];
        }
    }

    private static string DecodeValue(string value)
    {
        // Camel's RAW(...) syntax keeps the value verbatim.
        if (value.StartsWith("RAW(", StringComparison.Ordinal) && value.EndsWith(')'))
        {
            return value[4..^1];
        }

        return Uri.UnescapeDataString(value.Replace('+', ' '));
    }

    /// <summary>Returns the normalized URI: scheme, path and parameters sorted by name.</summary>
    public override string ToString() => Parameters.Count == 0
        ? $"{Scheme}:{Path}"
        : $"{Scheme}:{Path}?{string.Join('&', Parameters.Select(p => p.Key + "=" + p.Value))}";
}
