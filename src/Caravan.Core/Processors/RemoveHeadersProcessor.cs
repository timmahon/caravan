using Caravan.Spi;

namespace Caravan.Processors;

/// <summary>Removes headers matching a pattern, except excluded ones (<c>removeHeaders</c>).</summary>
public sealed class RemoveHeadersProcessor(string pattern, IReadOnlyList<string> excludePatterns) : IProcessor
{
    /// <summary>Exact name, wildcard (<c>foo*</c>) or regular expression.</summary>
    public string Pattern { get; } = pattern;

    /// <summary>Patterns of headers to keep.</summary>
    public IReadOnlyList<string> ExcludePatterns { get; } = excludePatterns;

    /// <inheritdoc />
    public ValueTask ProcessAsync(Exchange exchange)
    {
        exchange.Message.RemoveHeaders(Pattern, [.. ExcludePatterns]);
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public override string ToString() => $"RemoveHeaders[{Pattern}]";
}
