using Caravan.Model;
using Caravan.Spi;

namespace Caravan.Testing;

/// <summary>
/// Changes a route for a test (Camel's <c>AdviceWith</c>): replace its input, weave its nodes (replace, remove, add before or after), add
/// steps, intercept and mock endpoints, without touching the production route builder:
/// <code>
/// await AdviceWith.RouteAsync(context, "orders", a =&gt;
/// {
///     a.ReplaceFromWith("direct:start");
///     a.WeaveByToUri("http*").Replace(r =&gt; r.To("mock:http"));
/// });
/// </code>
/// Works before the context starts (the advised definition is created at start; use <c>UseAdviceWith</c> in
/// <see cref="CaravanTestSupport"/>) and after (the route is stopped, removed, changed and added again, so it restarts with the advice, as
/// in Camel). Nodes without an id get their generated id first, so ids are stable for <see cref="AdviceWithRouteBuilder.WeaveById"/>.
/// </summary>
public static class AdviceWith
{
    /// <summary>Advises the route <paramref name="routeId"/> (or the only route of the context when null).</summary>
    /// <param name="context">The context.</param>
    /// <param name="routeId">The route id, or null when the context has a single route.</param>
    /// <param name="configure">Describes the advice.</param>
    /// <param name="cancellationToken">Cancels stopping and restarting the route.</param>
    /// <returns>The advised route definition.</returns>
    /// <exception cref="ArgumentException">There is no such route (or no single route when <paramref name="routeId"/> is null).</exception>
    /// <exception cref="InvalidOperationException">A weave matched no node, or a change does not fit the definition tree.</exception>
    public static async Task<RouteDefinition> RouteAsync(
        ICaravanContext context,
        string? routeId,
        Action<AdviceWithRouteBuilder> configure,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(configure);
        var definitions = context.RouteDefinitions;
        RouteDefinition definition;
        if (routeId is null)
        {
            definition = definitions.Count == 1
                ? definitions[0]
                : throw new ArgumentException($"A route id is required to advise a context with {definitions.Count} routes", nameof(routeId));
        }
        else
        {
            definition = definitions.FirstOrDefault(d => d.Id == routeId) ?? throw new ArgumentException($"No route with id: {routeId}", nameof(routeId));
        }

        AssignIds(context, definition);
        var builder = new AdviceWithRouteBuilder(context, definition);
        configure(builder);

        var route = context.GetRoute(definition.Id!);
        if (route is null)
        {
            builder.Apply();
            return definition;
        }

        var wasStarted = route.Status is ServiceStatus.Started or ServiceStatus.Suspended;
        await context.StopRouteAsync(definition.Id!, cancellationToken).ConfigureAwait(false);
        await context.RemoveRouteAsync(definition.Id!, cancellationToken).ConfigureAwait(false);
        try
        {
            builder.Apply();
        }
        finally
        {
            await context.AddRoutesAsync(new AdvisedRoutesBuilder(definition), cancellationToken).ConfigureAwait(false);
            if (wasStarted && context.GetRouteStatus(definition.Id!) != ServiceStatus.Started)
            {
                await context.StartRouteAsync(definition.Id!, cancellationToken).ConfigureAwait(false);
            }
        }

        return definition;
    }

    /// <summary>Gives every node of the route without an id its generated id (Camel forces ids before advising).</summary>
    private static void AssignIds(ICaravanContext context, RouteDefinition definition)
    {
        foreach (var node in definition.AllNodes())
        {
            node.Id ??= context.NextNodeId(node.ShortName);
        }
    }
}
