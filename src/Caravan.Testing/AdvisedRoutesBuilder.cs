using Caravan.Builder;
using Caravan.Model;

namespace Caravan.Testing;

/// <summary>Adds an advised route definition back to its context.</summary>
internal sealed class AdvisedRoutesBuilder(RouteDefinition definition) : IRoutesBuilder
{
    public IReadOnlyList<RouteDefinition> Build(ICaravanContext context) => [definition];
}
