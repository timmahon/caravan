namespace Caravan.Spi;

/// <summary>
/// Supplies routes to create from templates when the context starts (Camel's <c>RouteTemplateParameterSource</c>), e.g. from configuration
/// (<see cref="Impl.ConfigurationRouteTemplateParameterSource"/>). Sources are taken from <see cref="ICaravanContext.RouteTemplateParameterSources"/>
/// and from the registry; a route whose id already exists is not created again.
/// </summary>
public interface IRouteTemplateParameterSource
{
    /// <summary>The parameter naming the template of a route (required in <see cref="Parameters"/>).</summary>
    const string TemplateIdParameter = "templateId";

    /// <summary>The ids of the routes to create.</summary>
    /// <returns>The route ids.</returns>
    IReadOnlyCollection<string> RouteIds();

    /// <summary>The parameters of a route, including <see cref="TemplateIdParameter"/>.</summary>
    /// <param name="routeId">The route id.</param>
    /// <returns>The parameters.</returns>
    IReadOnlyDictionary<string, object?> Parameters(string routeId);
}
