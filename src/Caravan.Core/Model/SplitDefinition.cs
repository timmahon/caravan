using Caravan.Aggregation;
using Caravan.Processors;
using Caravan.Spi;

namespace Caravan.Model;

/// <summary>
/// The <c>split</c> EIP: processes each part of the value of <see cref="Expression"/> with <see cref="Outputs"/>. Without an
/// aggregation strategy the exchange continues unchanged after the split, failing when a part failed (Camel's default:
/// <see cref="UseOriginalAggregationStrategy"/> propagating exceptions). See <see cref="SplitProcessor"/>.
/// </summary>
public sealed class SplitDefinition(IExpression expression) : FanOutDefinition
{
    /// <summary>Computes the parts.</summary>
    public IExpression Expression { get; set; } = expression;

    /// <summary>When set, a string value is split on it (by default a string is one part).</summary>
    public string? Delimiter { get; set; }

    /// <summary>The steps run for each part.</summary>
    public List<ProcessorDefinition> Outputs { get; } = [];

    /// <inheritdoc />
    public override IEnumerable<ProcessorDefinition> Children => Outputs;

    /// <inheritdoc />
    public override string ShortName => "split";

    /// <inheritdoc />
    public override string Label => $"split[{Expression}]";

    /// <inheritdoc />
    public override IProcessor CreateProcessor(RouteContext routeContext)
    {
        var settings = CreateSettings(routeContext, new UseOriginalAggregationStrategy(null, propagateException: true));
        return new SplitProcessor(Expression, routeContext.CreateBlockProcessor(Outputs, this), settings, Delimiter);
    }
}
