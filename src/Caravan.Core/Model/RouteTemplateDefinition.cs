namespace Caravan.Model;

/// <summary>
/// A route template (Camel's <c>routeTemplate</c>, the basis of Kamelets): a route definition with parameters and local beans, from which any
/// number of routes are created. Templates are stored in the context (<see cref="ICaravanContext.RouteTemplateDefinitions"/>) and are not routes
/// themselves; each route gets a deep copy of <see cref="Route"/> in which the parameters replace their <c>{{placeholders}}</c>.
/// </summary>
public sealed class RouteTemplateDefinition
{
    /// <summary>Creates a template.</summary>
    /// <param name="id">The template id.</param>
    public RouteTemplateDefinition(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        Id = id;
    }

    /// <summary>The template id.</summary>
    public string Id { get; }

    /// <summary>A description of the template.</summary>
    public string? Description { get; set; }

    /// <summary>The parameters, in declaration order.</summary>
    public List<RouteTemplateParameterDefinition> Parameters { get; } = [];

    /// <summary>The beans created for each route.</summary>
    public List<TemplateBeanDefinition> Beans { get; } = [];

    /// <summary>Called for each route before its beans are created, e.g. to compute parameters (Camel's <c>configure</c>).</summary>
    public Action<RouteTemplateContext>? Configurer { get; set; }

    /// <summary>The template's route; set by <see cref="Builder.RouteTemplateBuilder.From"/>.</summary>
    public RouteDefinition? Route { get; set; }

    /// <inheritdoc />
    public override string ToString() => $"RouteTemplate({Id})[{string.Join(", ", Parameters)}] {Route}";
}
