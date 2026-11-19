namespace Caravan.Model;

/// <summary>
/// A route to create from a template when its route builder is added to a context (Camel's <c>templatedRoute</c>), declared with
/// <see cref="Builder.RouteBuilder.TemplatedRoute"/>.
/// </summary>
public sealed class TemplatedRouteDefinition
{
    /// <summary>Creates a templated route.</summary>
    /// <param name="templateId">The template id.</param>
    public TemplatedRouteDefinition(string templateId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(templateId);
        TemplateId = templateId;
    }

    /// <summary>The template id.</summary>
    public string TemplateId { get; }

    /// <summary>The route id; null uses the template route's id, else a generated one.</summary>
    public string? RouteId { get; set; }

    /// <summary>The parameters.</summary>
    public Dictionary<string, object?> Parameters { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Local beans of the route, in addition to (or replacing, by name) the template's beans.</summary>
    public List<TemplateBeanDefinition> Beans { get; } = [];

    /// <summary>Called before the route's beans are created.</summary>
    public Action<RouteTemplateContext>? Configurer { get; set; }

    /// <summary>Changes the route's copy of the template's route before it is added.</summary>
    public Action<RouteDefinition>? Handler { get; set; }

    /// <summary>The template context of the route: its parameters, configurer, handler and beans (created later, per route).</summary>
    internal RouteTemplateContext CreateTemplateContext(ICaravanContext context)
    {
        var templateContext = new RouteTemplateContext(context) { Configurer = Configurer, Handler = Handler };
        foreach (var (name, value) in Parameters)
        {
            templateContext.SetParameter(name, value);
        }

        templateContext.Beans.AddRange(Beans);
        return templateContext;
    }

    /// <inheritdoc />
    public override string ToString() => $"TemplatedRoute({TemplateId} -> {RouteId ?? "?"})";
}
