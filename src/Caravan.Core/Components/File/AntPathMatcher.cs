namespace Caravan.Components.File;

/// <summary>Matches paths against Ant-style patterns (Camel's <c>AntPathMatcher</c>): <c>?</c> one character, <c>*</c> within a segment, <c>**</c> any segments.</summary>
internal static class AntPathMatcher
{
    /// <summary>Whether <paramref name="path"/> (separated by <c>/</c> or <c>\</c>) matches <paramref name="pattern"/>.</summary>
    public static bool Match(string pattern, string path, bool caseSensitive)
    {
        var patternParts = Split(pattern);
        var pathParts = Split(path);
        return MatchSegments(patternParts, 0, pathParts, 0, caseSensitive);
    }

    private static string[] Split(string value) => value.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);

    private static bool MatchSegments(string[] pattern, int pi, string[] path, int si, bool caseSensitive)
    {
        while (pi < pattern.Length)
        {
            if (pattern[pi] == "**")
            {
                // Collapse consecutive ** and try every split point.
                while (pi < pattern.Length && pattern[pi] == "**")
                {
                    pi++;
                }

                if (pi == pattern.Length)
                {
                    return true;
                }

                for (var k = si; k < path.Length; k++)
                {
                    if (MatchSegments(pattern, pi, path, k, caseSensitive))
                    {
                        return true;
                    }
                }

                return false;
            }

            if (si >= path.Length || !MatchSegment(pattern[pi], path[si], caseSensitive))
            {
                return false;
            }

            pi++;
            si++;
        }

        return si == path.Length;
    }

    private static bool MatchSegment(string pattern, string text, bool caseSensitive)
    {
        // Iterative wildcard matching with backtracking on the last '*'.
        int p = 0, t = 0, star = -1, mark = 0;
        while (t < text.Length)
        {
            if (p < pattern.Length && (pattern[p] == '?' || CharEquals(pattern[p], text[t], caseSensitive)))
            {
                p++;
                t++;
            }
            else if (p < pattern.Length && pattern[p] == '*')
            {
                star = p++;
                mark = t;
            }
            else if (star >= 0)
            {
                p = star + 1;
                t = ++mark;
            }
            else
            {
                return false;
            }
        }

        while (p < pattern.Length && pattern[p] == '*')
        {
            p++;
        }

        return p == pattern.Length;
    }

    private static bool CharEquals(char a, char b, bool caseSensitive) =>
        caseSensitive ? a == b : char.ToUpperInvariant(a) == char.ToUpperInvariant(b);
}
