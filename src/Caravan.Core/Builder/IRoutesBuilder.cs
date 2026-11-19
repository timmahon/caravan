using Caravan.Model;

namespace Caravan.Builder;

/// <summary>Something that adds routes to a context.</summary>
public interface IRoutesBuilder
{
    /// <summary>Builds the route definitions. Called once when the routes are added to <paramref name="context"/>.</summary>
    IReadOnlyList<RouteDefinition> Build(ICaravanContext context);
}
