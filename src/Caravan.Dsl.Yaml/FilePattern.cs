using System.Text;
using System.Text.RegularExpressions;

namespace Caravan.Dsl.Yaml;

/// <summary>
/// A file, a directory (its <c>*.yaml</c> and <c>*.yml</c> files) or a glob (<c>routes/*.yaml</c>, <c>routes/**/*.yaml</c>; <c>*</c> and
/// <c>?</c> within a path segment, <c>**</c> any number of directories), split into the directory to search and a matcher.
/// </summary>
internal sealed class FilePattern
{
    private readonly Regex _regex;

    private FilePattern(string directory, Regex regex, bool recursive, string? filter)
    {
        Directory = directory;
        _regex = regex;
        Recursive = recursive;
        Filter = filter;
    }

    /// <summary>The directory searched.</summary>
    public string Directory { get; }

    /// <summary>Whether subdirectories are searched.</summary>
    public bool Recursive { get; }

    /// <summary>A file name filter for a file system watcher, or null for all files.</summary>
    public string? Filter { get; }

    /// <summary>Parses <paramref name="pattern"/>, relative paths being relative to <paramref name="baseDirectory"/>.</summary>
    public static FilePattern Parse(string pattern, string baseDirectory)
    {
        var full = Path.GetFullPath(pattern, baseDirectory);
        if (System.IO.Directory.Exists(full))
        {
            return new FilePattern(full, YamlFiles(), recursive: false, filter: null);
        }

        var wildcard = full.IndexOfAny(['*', '?']);
        if (wildcard < 0)
        {
            var directory = Path.GetDirectoryName(full)!;
            var name = Path.GetFileName(full);
            return new FilePattern(directory, new Regex("^" + Regex.Escape(name) + "$", RegexOptions.CultureInvariant), recursive: false, filter: name);
        }

        var separator = full.LastIndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], wildcard);
        var root = full[..separator];
        var glob = full[(separator + 1)..].Replace('\\', '/');
        var regex = new StringBuilder("^");
        for (var i = 0; i < glob.Length; i++)
        {
            var c = glob[i];
            if (c == '*' && i + 1 < glob.Length && glob[i + 1] == '*')
            {
                i++;
                if (i + 1 < glob.Length && glob[i + 1] == '/')
                {
                    i++;
                    regex.Append("(?:.*/)?");
                }
                else
                {
                    regex.Append(".*");
                }
            }
            else
            {
                regex.Append(c switch
                {
                    '*' => "[^/]*",
                    '?' => "[^/]",
                    _ => Regex.Escape(c.ToString()),
                });
            }
        }

        regex.Append('$');
        var options = OperatingSystem.IsWindows() ? RegexOptions.IgnoreCase | RegexOptions.CultureInvariant : RegexOptions.CultureInvariant;
        return new FilePattern(root, new Regex(regex.ToString(), options), recursive: glob.Contains('/', StringComparison.Ordinal), filter: null);
    }

    /// <summary>The matching files, in ordinal order.</summary>
    public IEnumerable<string> Files() =>
        !System.IO.Directory.Exists(Directory)
            ? []
            : System.IO.Directory.EnumerateFiles(Directory, "*", Recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly)
                .Where(Matches)
                .Order(StringComparer.Ordinal);

    /// <summary>Whether <paramref name="path"/> (a full path) matches.</summary>
    public bool Matches(string path)
    {
        var relative = Path.GetRelativePath(Directory, path).Replace('\\', '/');
        return !relative.StartsWith("../", StringComparison.Ordinal) && _regex.IsMatch(relative);
    }

    private static Regex YamlFiles() => new(@"^[^/]*\.ya?ml$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
}
