using Caravan.Processors;
using Caravan.Spi;

namespace Caravan.Model;

/// <summary>The <c>setExchangePattern</c> EIP: changes the exchange's pattern (<c>InOnly</c>/<c>InOut</c>) for the rest of the route.</summary>
public sealed class SetExchangePatternDefinition(ExchangePattern pattern) : ProcessorDefinition
{
    /// <summary>The pattern to set.</summary>
    public ExchangePattern Pattern { get; set; } = pattern;

    /// <inheritdoc />
    public override string ShortName => "setExchangePattern";

    /// <inheritdoc />
    public override string Label => $"setExchangePattern[{Pattern}]";

    /// <inheritdoc />
    public override IProcessor CreateProcessor(RouteContext routeContext) => new SetExchangePatternProcessor(Pattern);
}
