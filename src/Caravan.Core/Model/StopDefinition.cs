using Caravan.Processors;
using Caravan.Spi;

namespace Caravan.Model;

/// <summary>The <c>stop</c> EIP: stops routing the exchange, including in the routes that called this one.</summary>
public sealed class StopDefinition : ProcessorDefinition
{
    /// <inheritdoc />
    public override string ShortName => "stop";

    /// <inheritdoc />
    public override IProcessor CreateProcessor(RouteContext routeContext) => new StopProcessor();
}
