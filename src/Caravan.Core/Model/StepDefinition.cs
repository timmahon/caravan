using Caravan.Processors;
using Caravan.Spi;

namespace Caravan.Model;

/// <summary>
/// The <c>step</c> EIP: a named block of steps. While inside, <see cref="ExchangePropertyKeys.StepId"/> holds the
/// step's <see cref="ProcessorDefinition.Id"/>.
/// </summary>
public sealed class StepDefinition : OutputDefinition
{
    /// <inheritdoc />
    public override string ShortName => "step";

    /// <inheritdoc />
    public override string Label => Id is null ? "step" : $"step[{Id}]";

    /// <inheritdoc />
    public override IProcessor CreateProcessor(RouteContext routeContext) =>
        new StepProcessor(Id ?? routeContext.Context.NextNodeId(ShortName), routeContext.CreateBlockProcessor(Outputs, this));
}
