using Caravan.Spi;

namespace Caravan.Processors;

/// <summary>
/// Runs an <c>intercept</c> block before a node (Camel's <c>intercept</c>): when the optional predicate matches, the block runs with the
/// <see cref="ExchangePropertyKeys.InterceptedRouteId"/>, <see cref="ExchangePropertyKeys.InterceptedNodeId"/> and
/// <see cref="ExchangePropertyKeys.InterceptedRouteEndpointUri"/> properties set; the node then runs unless the block failed or stopped the
/// exchange.
/// </summary>
public sealed class InterceptProcessor : INavigableProcessor
{
    private readonly IPredicate? _when;
    private readonly string _routeId;
    private readonly string? _nodeId;
    private readonly string? _routeEndpointUri;

    /// <summary>Creates the processor.</summary>
    /// <param name="when">Runs the block only for exchanges it matches; null for all.</param>
    /// <param name="block">The intercept block.</param>
    /// <param name="target">The intercepted node.</param>
    /// <param name="routeId">The id of the node's route.</param>
    /// <param name="nodeId">The id of the node.</param>
    /// <param name="routeEndpointUri">The URI of the route's endpoint.</param>
    public InterceptProcessor(IPredicate? when, IProcessor block, IProcessor target, string routeId, string? nodeId, string? routeEndpointUri)
    {
        _when = when;
        Block = block;
        Target = target;
        _routeId = routeId;
        _nodeId = nodeId;
        _routeEndpointUri = routeEndpointUri;
    }

    /// <summary>The intercept block.</summary>
    public IProcessor Block { get; }

    /// <summary>The intercepted node.</summary>
    public IProcessor Target { get; }

    /// <inheritdoc />
    public IReadOnlyList<IProcessor> Next => [Block, Target];

    /// <inheritdoc />
    public async ValueTask ProcessAsync(Exchange exchange)
    {
        if (_when is null || _when.Matches(exchange))
        {
            exchange.SetProperty(ExchangePropertyKeys.InterceptedRouteId, _routeId);
            exchange.SetProperty(ExchangePropertyKeys.InterceptedNodeId, _nodeId);
            exchange.SetProperty(ExchangePropertyKeys.InterceptedRouteEndpointUri, _routeEndpointUri);
            var flags = RoutingFlags.Capture(exchange);
            await Block.ProcessAsync(exchange).ConfigureAwait(false);
            if (flags.Interrupted(exchange))
            {
                return;
            }
        }

        await Target.ProcessAsync(exchange).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public override string ToString() => $"Intercept[{Block} -> {Target}]";
}
