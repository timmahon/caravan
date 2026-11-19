using Caravan.Processors;
using Caravan.Spi;

namespace Caravan.Model;

/// <summary>
/// Removes exchange properties matching a pattern (<c>removeProperties</c>). As in Camel, internal
/// <c>Caravan*</c> properties match too; exclude them explicitly if needed.
/// </summary>
public sealed class RemovePropertiesDefinition(string pattern, params string[] excludePatterns) : ProcessorDefinition
{
    /// <summary>Exact name, wildcard (<c>foo*</c>) or regular expression; matching ignores case.</summary>
    public string Pattern { get; set; } = pattern;

    /// <summary>Patterns of properties to keep.</summary>
    public List<string> ExcludePatterns { get; } = [.. excludePatterns];

    /// <inheritdoc />
    public override string ShortName => "removeProperties";

    /// <inheritdoc />
    public override string Label => $"removeProperties[{Pattern}]";

    /// <inheritdoc />
    public override IProcessor CreateProcessor(RouteContext routeContext) => new RemovePropertiesProcessor(Pattern, [.. ExcludePatterns]);
}
