using Caravan.Processors;
using Caravan.Spi;

namespace Caravan.Model;

/// <summary>
/// The <c>resequence</c> EIP: reorders exchanges by <see cref="Expression"/> and sends them, in order, through
/// <see cref="OutputDefinition.Outputs"/>; the incoming exchanges continue at once. <see cref="Config"/> selects the batch (default)
/// or stream algorithm. See <see cref="BatchResequencerProcessor"/> and <see cref="StreamResequencerProcessor"/>.
/// </summary>
public sealed class ResequenceDefinition(IExpression expression) : OutputDefinition
{
    /// <summary>Computes the sort key (batch) or the sequence number, a <see cref="long"/> (stream).</summary>
    public IExpression Expression { get; set; } = expression;

    /// <summary>The algorithm and its options; null for a batch resequencer with the default options.</summary>
    public ResequencerConfig? Config { get; set; }

    /// <inheritdoc />
    public override string ShortName => "resequence";

    /// <inheritdoc />
    public override string Label => $"resequence[{Expression}]";

    /// <inheritdoc />
    public override IProcessor CreateProcessor(RouteContext routeContext)
    {
        var block = routeContext.CreateBlockProcessor(Outputs, this);
        return Config switch
        {
            StreamResequencerConfig stream => new StreamResequencerProcessor(routeContext.Context, block, Expression, stream, routeContext.RouteId),
            BatchResequencerConfig batch => new BatchResequencerProcessor(routeContext.Context, block, Expression, batch, routeContext.RouteId),
            null => new BatchResequencerProcessor(routeContext.Context, block, Expression, new BatchResequencerConfig(), routeContext.RouteId),
            var other => throw new InvalidOperationException($"Unsupported resequencer configuration {other.GetType().Name}"),
        };
    }
}
