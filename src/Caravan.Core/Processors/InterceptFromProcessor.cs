using Caravan.Model;
using Caravan.Spi;
using Caravan.Support;

namespace Caravan.Processors;

/// <summary>
/// Runs the <c>interceptFrom</c> blocks matching a route's endpoint before the route's steps (Camel's <c>interceptFrom</c>): for each block
/// whose predicate matches, the header and property <see cref="CaravanHeaders.InterceptedEndpoint"/> are set to the route's endpoint URI and
/// the block runs; the route's steps run unless a block failed or stopped the exchange.
/// </summary>
public sealed class InterceptFromProcessor : INavigableProcessor
{
    private readonly IReadOnlyList<(IPredicate? When, IProcessor Block)> _blocks;
    private readonly string _endpointUri;

    private InterceptFromProcessor(IReadOnlyList<(IPredicate? When, IProcessor Block)> blocks, IProcessor pipeline, string endpointUri)
    {
        _blocks = blocks;
        Pipeline = pipeline;
        _endpointUri = endpointUri;
    }

    /// <summary>The route's steps.</summary>
    public IProcessor Pipeline { get; }

    /// <inheritdoc />
    public IReadOnlyList<IProcessor> Next => [.. _blocks.Select(b => b.Block), Pipeline];

    /// <summary>
    /// Wraps the route's <paramref name="pipeline"/> with the route's <c>interceptFrom</c> blocks whose pattern matches its endpoint; returns
    /// it as is when none does.
    /// </summary>
    /// <param name="routeContext">The route being built.</param>
    /// <param name="endpoint">The route's endpoint.</param>
    /// <param name="pipeline">The route's steps.</param>
    /// <returns>The processor to run.</returns>
    public static IProcessor Wrap(RouteContext routeContext, IEndpoint endpoint, IProcessor pipeline)
    {
        var blocks = new List<(IPredicate?, IProcessor)>();
        foreach (var definition in routeContext.Definition.InterceptFroms.Where(d => !d.Disabled && Matches(routeContext, endpoint, d)))
        {
            blocks.Add((definition.When, BuildBlock(routeContext, definition)));
        }

        return blocks.Count == 0 ? pipeline : new InterceptFromProcessor(blocks, pipeline, endpoint.EndpointUri);
    }

    /// <inheritdoc />
    public async ValueTask ProcessAsync(Exchange exchange)
    {
        foreach (var (when, block) in _blocks)
        {
            if (when is not null && !when.Matches(exchange))
            {
                continue;
            }

            exchange.Message.SetHeader(CaravanHeaders.InterceptedEndpoint, _endpointUri);
            exchange.SetProperty(ExchangePropertyKeys.InterceptedEndpoint, _endpointUri);
            var flags = RoutingFlags.Capture(exchange);
            await block.ProcessAsync(exchange).ConfigureAwait(false);
            if (flags.Interrupted(exchange))
            {
                return;
            }
        }

        await Pipeline.ProcessAsync(exchange).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public override string ToString() => $"InterceptFrom[{_endpointUri}]";

    /// <summary>Builds an intercept block of the route (registered as a route service).</summary>
    internal static IProcessor BuildBlock(RouteContext routeContext, InterceptDefinition definition)
    {
        definition.Id ??= routeContext.Context.NextNodeId(definition.ShortName);
        var block = definition.CreateProcessor(routeContext);
        routeContext.AddService(block);
        return block;
    }

    private static bool Matches(RouteContext routeContext, IEndpoint endpoint, InterceptFromDefinition definition) =>
        definition.Uri is not { } pattern ||
        EndpointHelper.MatchEndpoint(routeContext.Context, endpoint.EndpointUri, pattern) ||
        EndpointHelper.MatchEndpoint(routeContext.Context, routeContext.Definition.FromUri, pattern);
}
