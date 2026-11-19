using Caravan.Support;

namespace Caravan.Aggregation;

/// <summary>
/// Merges data retrieved by the <c>claimCheck</c> EIP (<c>Get</c>, <c>GetAndRemove</c>, <c>Pop</c>) back into the exchange (Camel's
/// <c>ClaimCheckAggregationStrategy</c>), as selected by <see cref="Filter"/>, a comma-separated list of:
/// <list type="bullet">
/// <item><c>body</c> (or <c>+body</c>): the body; <c>-body</c>: not the body.</item>
/// <item><c>headers</c> (or <c>+headers</c>): all headers; <c>-headers</c>: no headers.</item>
/// <item><c>header:pattern</c> (or <c>+header:pattern</c>): the headers matching the pattern (exact name, wildcard <c>*</c> or regex, see
/// <see cref="PatternHelper"/>); <c>-header:pattern</c>: all headers except those matching.</item>
/// </list>
/// Without a filter (or with <c>*</c>) the body and all headers are merged back; a filter of only exclusions merges back everything else. Merged
/// headers are added to (or replace) the exchange's headers; others are kept.
/// </summary>
public sealed class ClaimCheckAggregationStrategy : IAggregationStrategy
{
    private readonly string[] _parts;

    /// <summary>Creates the strategy.</summary>
    /// <param name="filter">What to merge back; null or empty for everything.</param>
    /// <exception cref="ArgumentException">The filter has an invalid part.</exception>
    public ClaimCheckAggregationStrategy(string? filter = null)
    {
        Filter = string.IsNullOrWhiteSpace(filter) ? null : filter;
        _parts = Filter is null ? [] : Filter.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in _parts)
        {
            if (!IsValid(part))
            {
                throw new ArgumentException(
                    $"Invalid claim check filter part '{part}': expected body, headers or header:pattern, optionally prefixed with + or -",
                    nameof(filter));
            }
        }
    }

    /// <summary>What to merge back; null for everything.</summary>
    public string? Filter { get; }

    /// <inheritdoc />
    public Exchange? Aggregate(Exchange? oldExchange, Exchange newExchange)
    {
        if (oldExchange is null)
        {
            return newExchange;
        }

        var target = oldExchange.Message;
        var source = newExchange.Message;
        if (Filter is null || Filter == "*")
        {
            target.Body = source.Body;
            foreach (var (name, value) in source.Headers)
            {
                target.Headers[name] = value;
            }

            return oldExchange;
        }

        if (IsBodyEnabled())
        {
            target.Body = source.Body;
        }

        if (IsHeadersEnabled())
        {
            if (HasHeaderPatterns())
            {
                // Only the headers matching an include pattern.
                foreach (var (name, value) in source.Headers)
                {
                    if (MatchesHeaderPattern(name))
                    {
                        target.Headers[name] = value;
                    }
                }
            }
            else
            {
                // All headers except the excluded ones.
                foreach (var (name, value) in source.Headers)
                {
                    if (!IsExcludedHeader(name))
                    {
                        target.Headers[name] = value;
                    }
                }
            }
        }

        return oldExchange;
    }

    /// <inheritdoc />
    public override string ToString() => $"ClaimCheckAggregationStrategy[{Filter ?? "*"}]";

    private static bool IsValid(string part)
    {
        var p = part.StartsWith('+') || part.StartsWith('-') ? part[1..] : part;
        return p is "body" or "headers" || (p.StartsWith("header:", StringComparison.Ordinal) && p.Length > "header:".Length);
    }

    private bool IsBodyEnabled()
    {
        var onlyExclude = true;
        foreach (var part in _parts)
        {
            if (part is "body" or "+body")
            {
                return true;
            }

            if (part == "-body")
            {
                return false;
            }

            onlyExclude &= part.StartsWith('-');
        }

        // The body is merged back when the filter only excludes.
        return onlyExclude;
    }

    private bool IsHeadersEnabled()
    {
        var onlyExclude = true;
        foreach (var part in _parts)
        {
            if (part.StartsWith("header:", StringComparison.Ordinal) || part.StartsWith("+header:", StringComparison.Ordinal) ||
                part.StartsWith("-header:", StringComparison.Ordinal) || part is "headers" or "+headers")
            {
                return true;
            }

            if (part == "-headers")
            {
                return false;
            }

            onlyExclude &= part.StartsWith('-');
        }

        // The headers are merged back when the filter only excludes.
        return onlyExclude;
    }

    private bool HasHeaderPatterns() =>
        _parts.Any(p => p.StartsWith("header:", StringComparison.Ordinal) || p.StartsWith("+header:", StringComparison.Ordinal));

    private bool MatchesHeaderPattern(string name) =>
        _parts.Any(p =>
            (p.StartsWith("header:", StringComparison.Ordinal) || p.StartsWith("+header:", StringComparison.Ordinal)) &&
            PatternHelper.Matches(name, p[(p.IndexOf(':', StringComparison.Ordinal) + 1)..]));

    private bool IsExcludedHeader(string name) =>
        _parts.Any(p => p.StartsWith("-header:", StringComparison.Ordinal) && PatternHelper.Matches(name, p["-header:".Length..]));
}
