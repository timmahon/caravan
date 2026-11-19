namespace Caravan.Components.File;

/// <summary>
/// The filter of the <c>antInclude</c>/<c>antExclude</c> options (Camel's <c>AntPathMatcherGenericFileFilter</c>): a file is accepted when its path
/// relative to the starting directory matches one of the include patterns (all when there are none) and none of the exclude patterns.
/// Directories are always accepted, so recursion reaches the files below them.
/// </summary>
/// <typeparam name="T">The type of the underlying file object.</typeparam>
public sealed class AntPathMatcherGenericFileFilter<T> : IGenericFileFilter<T>
{
    private readonly string[] _includes;
    private readonly string[] _excludes;

    /// <summary>Creates the filter.</summary>
    /// <param name="includes">Comma-separated include patterns, or null.</param>
    /// <param name="excludes">Comma-separated exclude patterns, or null.</param>
    /// <param name="caseSensitive">Whether matching is case-sensitive.</param>
    public AntPathMatcherGenericFileFilter(string? includes, string? excludes, bool caseSensitive = true)
    {
        _includes = SplitPatterns(includes);
        _excludes = SplitPatterns(excludes);
        CaseSensitive = caseSensitive;
    }

    /// <summary>Whether matching is case-sensitive.</summary>
    public bool CaseSensitive { get; }

    /// <summary>Whether <paramref name="path"/> (relative, any separator) is accepted.</summary>
    /// <param name="path">The relative path.</param>
    public bool Accept(string path)
    {
        if (_excludes.Any(p => AntPathMatcher.Match(p, path, CaseSensitive)))
        {
            return false;
        }

        return _includes.Length == 0 || _includes.Any(p => AntPathMatcher.Match(p, path, CaseSensitive));
    }

    /// <inheritdoc />
    public bool Accept(GenericFile<T> file) => file.IsDirectory || Accept(file.RelativeFilePath);

    private static string[] SplitPatterns(string? patterns) =>
        patterns is null ? [] : patterns.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
