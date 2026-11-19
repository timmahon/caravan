namespace Caravan.Components.File;

/// <summary>Path helpers of the file components (Camel's <c>FileUtil</c>).</summary>
internal static class FileUtil
{
    /// <summary>Replaces both separators with the platform's (Camel's <c>normalizePath</c>).</summary>
    public static string Normalize(string path) =>
        Path.DirectorySeparatorChar == '/' ? path.Replace('\\', '/') : path.Replace('/', Path.DirectorySeparatorChar);

    /// <summary>The name without leading directories.</summary>
    public static string StripPath(string name)
    {
        var pos = LastSeparator(name);
        return pos < 0 ? name : name[(pos + 1)..];
    }

    /// <summary>The leading directories of <paramref name="name"/>, or null when there are none.</summary>
    public static string? OnlyPath(string name)
    {
        var pos = LastSeparator(name);
        return pos switch
        {
            < 0 => null,
            0 => name[..1],
            _ => name[..pos],
        };
    }

    /// <summary>
    /// The name without its extension: from the first dot of the last segment, or the last dot with <paramref name="single"/>
    /// (Camel's <c>stripExt</c>).
    /// </summary>
    public static string StripExt(string name, bool single)
    {
        var sep = LastSeparator(name);
        var only = name[(sep + 1)..];
        var dot = single ? only.LastIndexOf('.') : only.IndexOf('.');
        return dot > 0 ? name[..(sep + 1 + dot)] : name;
    }

    /// <summary>
    /// The extension of the last segment: after its first dot, or after the last with <paramref name="single"/>; null without dot
    /// (Camel's <c>onlyExt</c>).
    /// </summary>
    public static string? OnlyExt(string name, bool single)
    {
        var only = StripPath(name);
        var dot = single ? only.LastIndexOf('.') : only.IndexOf('.');
        return dot >= 0 ? only[(dot + 1)..] : null;
    }

    /// <summary>Removes trailing separators (keeps a lone root).</summary>
    public static string StripTrailingSeparator(string path)
    {
        var end = path.Length;
        while (end > 1 && path[end - 1] is '/' or '\\')
        {
            end--;
        }

        return path[..end];
    }

    /// <summary>Whether <paramref name="path"/> is under <paramref name="directory"/> (both full paths) or is it.</summary>
    public static bool IsUnder(string path, string directory)
    {
        var comparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var dir = Path.TrimEndingDirectorySeparator(directory);
        return path.Equals(dir, comparison) ||
               (path.StartsWith(dir, comparison) && path.Length > dir.Length && path[dir.Length] == Path.DirectorySeparatorChar) ||
               (dir.Length > 0 && dir[^1] == Path.DirectorySeparatorChar && path.StartsWith(dir, comparison));
    }

    /// <summary>Decodes the escapes <c>\n</c>, <c>\r</c> and <c>\t</c> (written <c>\\n</c> in URIs, as in Camel's <c>appendChars</c>).</summary>
    public static string Unescape(string text) =>
        text.Replace("\\\\n", "\n", StringComparison.Ordinal)
            .Replace("\\\\r", "\r", StringComparison.Ordinal)
            .Replace("\\\\t", "\t", StringComparison.Ordinal)
            .Replace("\\n", "\n", StringComparison.Ordinal)
            .Replace("\\r", "\r", StringComparison.Ordinal)
            .Replace("\\t", "\t", StringComparison.Ordinal);

    private static int LastSeparator(string name) => Math.Max(name.LastIndexOf('/'), name.LastIndexOf('\\'));
}
