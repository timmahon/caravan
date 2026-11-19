using Caravan.Model;

namespace Caravan.Testing;

/// <summary>AdviceWith as an extension of the context: <c>await context.AdviceWithAsync("orders", a =&gt; ...)</c>.</summary>
public static class AdviceWithContextExtensions
{
    /// <summary>Advises the route <paramref name="routeId"/> (or the only route when null); see <see cref="AdviceWith.RouteAsync"/>.</summary>
    /// <param name="context">The context.</param>
    /// <param name="routeId">The route id, or null when the context has a single route.</param>
    /// <param name="configure">Describes the advice.</param>
    /// <param name="cancellationToken">Cancels stopping and restarting the route.</param>
    /// <returns>The advised route definition.</returns>
    public static Task<RouteDefinition> AdviceWithAsync(
        this ICaravanContext context,
        string? routeId,
        Action<AdviceWithRouteBuilder> configure,
        CancellationToken cancellationToken = default) =>
        AdviceWith.RouteAsync(context, routeId, configure, cancellationToken);
}
