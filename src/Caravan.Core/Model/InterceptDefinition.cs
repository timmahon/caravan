using Caravan.Spi;

namespace Caravan.Model;

/// <summary>
/// The <c>intercept</c> EIP: <see cref="OutputDefinition.Outputs"/> run before every node of every route it applies to (the routes of the
/// route builder that defines it). When <see cref="When"/> is set the block only runs for exchanges it matches. A <c>stop</c> in the block
/// stops the exchange: the intercepted node and the rest of the route do not run.
/// </summary>
/// <remarks>
/// Nodes of <c>intercept</c>, <c>interceptFrom</c>, <c>interceptSendToEndpoint</c>, <c>onException</c> and <c>onCompletion</c> blocks are not
/// intercepted. The block's nodes are wrapped by the route's error handler like any other node; the intercepted node keeps its own.
/// </remarks>
public class InterceptDefinition : OutputDefinition
{
    /// <summary>When set, the block only runs for exchanges matching it.</summary>
    public IPredicate? When { get; set; }

    /// <inheritdoc />
    public override string ShortName => "intercept";

    /// <summary>Builds the block (the intercepting processor is created per node by the route).</summary>
    public override IProcessor CreateProcessor(RouteContext routeContext) => routeContext.CreateBlockProcessor(Outputs, this);
}
