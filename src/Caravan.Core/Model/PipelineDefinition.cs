using Caravan.Spi;

namespace Caravan.Model;

/// <summary>The <c>pipeline</c> EIP: an explicit block of steps run in sequence.</summary>
public sealed class PipelineDefinition : OutputDefinition
{
    /// <inheritdoc />
    public override string ShortName => "pipeline";

    /// <inheritdoc />
    public override IProcessor CreateProcessor(RouteContext routeContext) => routeContext.CreateBlockProcessor(Outputs, this);
}
