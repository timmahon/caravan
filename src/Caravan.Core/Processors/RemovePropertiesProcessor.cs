using Caravan.Spi;

namespace Caravan.Processors;

/// <summary>Removes exchange properties matching a pattern, except excluded ones (<c>removeProperties</c>).</summary>
public sealed class RemovePropertiesProcessor(string pattern, IReadOnlyList<string> excludePatterns) : IProcessor
{
    /// <summary>Exact name, wildcard (<c>foo*</c>) or regular expression.</summary>
    public string Pattern { get; } = pattern;

    /// <summary>Patterns of properties to keep.</summary>
    public IReadOnlyList<string> ExcludePatterns { get; } = excludePatterns;

    /// <inheritdoc />
    public ValueTask ProcessAsync(Exchange exchange)
    {
        exchange.RemoveProperties(Pattern, [.. ExcludePatterns]);
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public override string ToString() => $"RemoveProperties[{Pattern}]";
}
