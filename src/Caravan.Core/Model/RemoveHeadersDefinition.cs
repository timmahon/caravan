using Caravan.Processors;
using Caravan.Spi;

namespace Caravan.Model;

/// <summary>Removes headers matching a pattern (<c>removeHeaders</c>).</summary>
public sealed class RemoveHeadersDefinition(string pattern, params string[] excludePatterns) : ProcessorDefinition
{
    /// <summary>Exact name, wildcard (<c>foo*</c>) or regular expression; matching ignores case.</summary>
    public string Pattern { get; set; } = pattern;

    /// <summary>Patterns of headers to keep.</summary>
    public List<string> ExcludePatterns { get; } = [.. excludePatterns];

    /// <inheritdoc />
    public override string ShortName => "removeHeaders";

    /// <inheritdoc />
    public override string Label => $"removeHeaders[{Pattern}]";

    /// <inheritdoc />
    public override IProcessor CreateProcessor(RouteContext routeContext) => new RemoveHeadersProcessor(Pattern, [.. ExcludePatterns]);
}
