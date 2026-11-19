namespace Caravan.Support;

/// <summary>Media type matching for the REST DSL (<c>Content-Type</c> against <c>consumes</c>, <c>Accept</c> against <c>produces</c>).</summary>
public static class MediaTypeHelper
{
    /// <summary>Splits a comma-separated list of media types, dropping parameters (<c>;charset=...</c>, <c>;q=...</c>) and blanks.</summary>
    /// <param name="list">The list, or null.</param>
    /// <returns>The media types in lower case.</returns>
    public static IReadOnlyList<string> Split(string? list) => string.IsNullOrWhiteSpace(list)
        ? []
        : [.. list.Split(',').Select(t => t.Split(';')[0].Trim().ToLowerInvariant()).Where(t => t.Length > 0)];

    /// <summary>
    /// Whether any media type of <paramref name="actual"/> matches any of <paramref name="allowed"/>, wildcards (<c>*/*</c>,
    /// <c>application/*</c>) on either side included. An empty list on either side matches.
    /// </summary>
    /// <param name="allowed">The allowed media types (comma-separated).</param>
    /// <param name="actual">The media types to check (comma-separated, e.g. an <c>Accept</c> header).</param>
    /// <returns>Whether they match.</returns>
    public static bool Matches(string? allowed, string? actual)
    {
        var a = Split(allowed);
        var b = Split(actual);
        return a.Count == 0 || b.Count == 0 || a.Any(x => b.Any(y => Match(x, y)));
    }

    private static bool Match(string x, string y)
    {
        if (x == "*/*" || y == "*/*" || x == "*" || y == "*")
        {
            return true;
        }

        var (xt, xs) = Parts(x);
        var (yt, ys) = Parts(y);
        return xt == yt && (xs == "*" || ys == "*" || xs == ys);
    }

    private static (string Type, string SubType) Parts(string mediaType)
    {
        var slash = mediaType.IndexOf('/');
        return slash < 0 ? (mediaType, "*") : (mediaType[..slash], mediaType[(slash + 1)..]);
    }
}
