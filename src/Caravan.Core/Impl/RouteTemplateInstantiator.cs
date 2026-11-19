using Caravan.Model;
using Caravan.Support;

namespace Caravan.Impl;

/// <summary>
/// Creates the definition of a route from a route template (Camel's <c>DefaultModel.addRouteFromTemplate</c>): applies defaults and
/// configurers, checks required parameters, creates the local beans, then deep-copies the template's route with the parameters substituted.
/// </summary>
internal static class RouteTemplateInstantiator
{
    /// <summary>The URI of the <c>from</c> endpoint of templates used as Kamelets.</summary>
    public const string KameletSource = "kamelet:source";

    /// <summary>Creates the route definition.</summary>
    /// <param name="context">The context.</param>
    /// <param name="template">The template.</param>
    /// <param name="routeId">The requested route id, or null.</param>
    /// <param name="templateContext">The parameters and beans.</param>
    /// <returns>The route definition, not yet added to the context.</returns>
    /// <exception cref="FailedToCreateRouteFromTemplateException">The route cannot be created.</exception>
    public static RouteDefinition Create(CaravanContext context, RouteTemplateDefinition template, string? routeId, RouteTemplateContext templateContext)
    {
        try
        {
            return DoCreate(context, template, routeId, templateContext);
        }
        catch (Exception e) when (e is not FailedToCreateRouteFromTemplateException)
        {
            throw new FailedToCreateRouteFromTemplateException(routeId ?? templateContext.RouteId, template.Id, e.Message, e);
        }
    }

    private static RouteDefinition DoCreate(CaravanContext context, RouteTemplateDefinition template, string? routeId, RouteTemplateContext templateContext)
    {
        var route = template.Route ?? throw new FailedToCreateRouteFromTemplateException(routeId, template.Id, "The template has no route (call From)");
        templateContext.TemplateId = template.Id;
        foreach (var parameter in template.Parameters)
        {
            if (!templateContext.HasParameter(parameter.Name) && parameter.DefaultValue is not null)
            {
                templateContext.Parameters[parameter.Name] = parameter.DefaultValue;
            }
        }

        template.Configurer?.Invoke(templateContext);
        templateContext.Configurer?.Invoke(templateContext);
        var missing = template.Parameters.Where(p => p.Required && !templateContext.HasParameter(p.Name)).Select(p => p.Name).ToList();
        if (missing.Count > 0)
        {
            throw new FailedToCreateRouteFromTemplateException(
                routeId,
                template.Id,
                $"Route template {template.Id} the following mandatory parameters must be provided: {string.Join(", ", missing)}");
        }

        routeId ??= route.Id is { } templateRouteId
            ? context.ResolvePropertyPlaceholders(new TemplateParameterSubstitution(templateContext.Parameters).Apply(templateRouteId))
            : context.GenerateRouteId();
        templateContext.RouteId = routeId;

        foreach (var bean in template.Beans.Concat(templateContext.Beans))
        {
            templateContext.LocalBeans.Bind(bean.Name, bean.Factory(templateContext));
        }

        // A local bean is referenced in URIs as {{name}}: give it a name unique to the route (endpoints are cached per URI).
        foreach (var name in templateContext.LocalBeans.Names.ToList())
        {
            var unique = $"{name}-{routeId}";
            templateContext.LocalBeans.Bind(unique, templateContext.LocalBeans.Lookup(name)!);
            if (!templateContext.Parameters.ContainsKey(name))
            {
                templateContext.Parameters[name] = unique;
            }
        }

        var substitution = new TemplateParameterSubstitution(templateContext.Parameters);
        var copy = DefinitionCloner.Clone(route, substitution.Apply);
        copy.Id = routeId;
        copy.TemplateId = template.Id;
        copy.TemplateParameters = new Dictionary<string, object?>(templateContext.Parameters, StringComparer.OrdinalIgnoreCase);
        copy.LocalBeans = templateContext.LocalBeans.Names.Count > 0 ? templateContext.LocalBeans : null;
        copy.IsKamelet = templateContext.IsKamelet;
        if (EndpointUri.Parse(copy.FromUri) is { Scheme: "kamelet", Path: "source" })
        {
            copy.FromUri = $"{KameletSource}?routeId={Uri.EscapeDataString(routeId)}";
        }

        templateContext.Handler?.Invoke(copy);
        return copy;
    }
}
