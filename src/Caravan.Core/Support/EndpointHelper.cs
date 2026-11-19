namespace Caravan.Support;

/// <summary>Endpoint URI helpers (Camel's <c>EndpointHelper</c>).</summary>
public static class EndpointHelper
{
    /// <summary>
    /// Whether the endpoint <paramref name="uri"/> matches <paramref name="pattern"/>, as Camel's <c>EndpointHelper.matchEndpoint</c>: the
    /// pattern may be an exact URI (compared after normalization, so <c>direct://a</c> matches <c>direct:a</c> and parameter order does not
    /// matter), a wildcard (<c>seda:*</c>, <c>*</c>) or a regular expression (<c>(direct|seda):.*</c>), matched against the normalized URI and
    /// against the URI without its parameters. Property placeholders in the pattern are resolved.
    /// </summary>
    /// <param name="context">The context resolving placeholders, or null.</param>
    /// <param name="uri">The endpoint URI.</param>
    /// <param name="pattern">The pattern.</param>
    /// <returns>Whether it matches.</returns>
    public static bool MatchEndpoint(ICaravanContext? context, string uri, string pattern)
    {
        ArgumentNullException.ThrowIfNull(uri);
        ArgumentNullException.ThrowIfNull(pattern);
        if (context is not null)
        {
            pattern = context.ResolvePropertyPlaceholders(pattern);
        }

        if (string.Equals(uri, pattern, StringComparison.OrdinalIgnoreCase) || PatternHelper.Matches(uri, pattern))
        {
            return true;
        }

        var normalized = TryNormalize(uri) ?? uri;
        if (PatternHelper.Matches(normalized, pattern))
        {
            return true;
        }

        if (TryNormalize(pattern) is { } normalizedPattern &&
            (string.Equals(normalized, normalizedPattern, StringComparison.OrdinalIgnoreCase) || PatternHelper.Matches(normalized, normalizedPattern)))
        {
            return true;
        }

        var query = normalized.IndexOf('?', StringComparison.Ordinal);
        return query > 0 && PatternHelper.Matches(normalized[..query], pattern);
    }

    /// <summary>
    /// The URI of the mock endpoint standing for <paramref name="uri"/> when mocking endpoints (Camel's <c>mockEndpoints</c>): <c>mock:</c>
    /// followed by the normalized URI without its parameters, e.g. <c>mock:direct:foo</c> for <c>direct://foo?x=1</c>.
    /// </summary>
    /// <param name="uri">The endpoint URI.</param>
    /// <returns>The mock endpoint URI.</returns>
    public static string MockEndpointUri(string uri)
    {
        var normalized = TryNormalize(uri) ?? uri.Replace("://", ":", StringComparison.Ordinal);
        var query = normalized.IndexOf('?', StringComparison.Ordinal);
        return "mock:" + (query >= 0 ? normalized[..query] : normalized);
    }

    private static string? TryNormalize(string uri)
    {
        try
        {
            return EndpointUri.Normalize(uri);
        }
        catch (Exception e) when (e is ResolveEndpointFailedException or ArgumentException or FormatException)
        {
            return null;
        }
    }
}
