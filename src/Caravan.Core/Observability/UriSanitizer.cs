using System.Text;

namespace Caravan.Observability;

/// <summary>
/// Removes secrets from endpoint URIs before they appear in spans, metrics, logs or management (Camel's <c>URISupport.sanitizeUri</c>):
/// the values of query options whose name looks sensitive (password, secret, token, key, ...) and the password of user info are replaced
/// with <c>xxxxxx</c>.
/// </summary>
public static class UriSanitizer
{
    /// <summary>The replacement of secret values.</summary>
    public const string Mask = "xxxxxx";

    private static readonly string[] SensitiveFragments =
    [
        "password", "passwd", "passphrase", "secret", "token", "accesskey", "apikey", "api-key", "api_key", "privatekey", "credential",
        "authorization", "signature", "sastoken", "clientsecret",
    ];

    /// <summary>Whether a query option name is considered sensitive.</summary>
    /// <param name="name">The option name.</param>
    /// <returns>True when its value must be masked.</returns>
    public static bool IsSensitive(string name)
    {
        var lower = name.ToLowerInvariant();
        foreach (var fragment in SensitiveFragments)
        {
            if (lower.Contains(fragment, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Returns <paramref name="uri"/> with sensitive option values and user-info passwords masked.</summary>
    /// <param name="uri">The URI (may be null).</param>
    /// <returns>The sanitized URI.</returns>
    public static string? Sanitize(string? uri)
    {
        if (string.IsNullOrEmpty(uri))
        {
            return uri;
        }

        var query = uri.IndexOf('?', StringComparison.Ordinal);
        var path = query < 0 ? uri : uri[..query];
        path = SanitizeUserInfo(path);
        if (query < 0)
        {
            return path;
        }

        var builder = new StringBuilder(path).Append('?');
        var first = true;
        foreach (var part in uri[(query + 1)..].Split('&'))
        {
            if (!first)
            {
                builder.Append('&');
            }

            first = false;
            var eq = part.IndexOf('=', StringComparison.Ordinal);
            if (eq > 0 && IsSensitive(part[..eq]))
            {
                builder.Append(part, 0, eq + 1).Append(Mask);
            }
            else
            {
                builder.Append(part);
            }
        }

        return builder.ToString();
    }

    /// <summary>Returns <paramref name="uri"/> without its query options and with secrets masked (a low-cardinality endpoint name).</summary>
    /// <param name="uri">The URI.</param>
    /// <returns>The sanitized URI without options.</returns>
    public static string WithoutQuery(string uri)
    {
        var query = uri.IndexOf('?', StringComparison.Ordinal);
        return SanitizeUserInfo(query < 0 ? uri : uri[..query]);
    }

    private static string SanitizeUserInfo(string path)
    {
        // scheme://user:password@host/...
        var scheme = path.IndexOf("://", StringComparison.Ordinal);
        if (scheme < 0)
        {
            return path;
        }

        var authorityStart = scheme + 3;
        var at = path.IndexOf('@', authorityStart);
        if (at < 0)
        {
            return path;
        }

        var slash = path.IndexOf('/', authorityStart);
        if (slash >= 0 && slash < at)
        {
            return path;
        }

        var colon = path.IndexOf(':', authorityStart);
        return colon < 0 || colon > at ? path : string.Concat(path.AsSpan(0, colon + 1), Mask, path.AsSpan(at));
    }
}
