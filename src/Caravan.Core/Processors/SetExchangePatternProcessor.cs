using Caravan.Spi;

namespace Caravan.Processors;

/// <summary>The <c>setExchangePattern</c> EIP: changes the exchange's pattern for the rest of the route.</summary>
public sealed class SetExchangePatternProcessor(ExchangePattern pattern) : IProcessor
{
    /// <summary>The pattern to set.</summary>
    public ExchangePattern Pattern { get; } = pattern;

    /// <inheritdoc />
    public ValueTask ProcessAsync(Exchange exchange)
    {
        exchange.Pattern = Pattern;
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public override string ToString() => $"SetExchangePattern[{Pattern}]";
}
