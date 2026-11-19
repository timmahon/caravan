using Caravan.Model;

namespace Caravan.Spi;

/// <summary>Creates a route policy for each route of the context (Camel's <c>RoutePolicyFactory</c>), see <see cref="ICaravanContext.RoutePolicyFactories"/>.</summary>
public interface IRoutePolicyFactory
{
    /// <summary>Creates the policy of a route being created, or null for none.</summary>
    /// <param name="context">The context.</param>
    /// <param name="routeId">The route id.</param>
    /// <param name="route">The route's definition.</param>
    /// <returns>The policy, or null.</returns>
    IRoutePolicy? CreateRoutePolicy(ICaravanContext context, string routeId, RouteDefinition route);
}
