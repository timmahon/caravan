using Caravan.Spi;
using Microsoft.Extensions.Configuration;

namespace Caravan.Impl;

/// <summary>
/// An <see cref="IRouteTemplateParameterSource"/> reading templated routes from an <see cref="IConfiguration"/> section (Camel main's
/// <c>camel.route-template</c> properties). A context created with a service provider whose <see cref="IConfiguration"/> has a
/// <c>caravan:templatedRoutes</c> section uses it automatically. Each child is a route, keyed by its id (or with a <c>routeId</c> value):
/// <code>
/// "caravan": { "templatedRoutes": {
///   "fast": { "templateId": "timerTemplate", "parameters": { "name": "fast", "period": "100" } },
///   "slow": { "templateId": "timerTemplate", "name": "slow" }
/// } }
/// </code>
/// Parameters are the values of the <c>parameters</c> child or, without one, the other values of the route. The section is read when the context
/// starts.
/// </summary>
public sealed class ConfigurationRouteTemplateParameterSource : IRouteTemplateParameterSource
{
    /// <summary>The section a context reads by default.</summary>
    public const string DefaultSection = "caravan:templatedRoutes";

    private readonly IConfiguration _section;

    /// <summary>Creates a source reading the routes from <paramref name="section"/>.</summary>
    /// <param name="section">The section holding one child per route, e.g. <c>configuration.GetSection("caravan:templatedRoutes")</c>.</param>
    public ConfigurationRouteTemplateParameterSource(IConfiguration section)
    {
        ArgumentNullException.ThrowIfNull(section);
        _section = section;
    }

    /// <inheritdoc />
    public IReadOnlyCollection<string> RouteIds() => [.. _section.GetChildren().Select(RouteId)];

    /// <inheritdoc />
    public IReadOnlyDictionary<string, object?> Parameters(string routeId)
    {
        var route = _section.GetChildren().FirstOrDefault(c => RouteId(c) == routeId)
            ?? throw new ArgumentException($"No templated route with id {routeId} in configuration section {(_section as IConfigurationSection)?.Path}", nameof(routeId));
        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        var parameters = route.GetSection("parameters");
        var values = parameters.Exists() ? parameters.GetChildren() : route.GetChildren().Where(c => c.Key is not "routeId" and not IRouteTemplateParameterSource.TemplateIdParameter);
        foreach (var value in values.Where(v => v.Value is not null))
        {
            result[value.Key] = value.Value;
        }

        result[IRouteTemplateParameterSource.TemplateIdParameter] = route[IRouteTemplateParameterSource.TemplateIdParameter];
        return result;
    }

    private static string RouteId(IConfigurationSection route) => route["routeId"] ?? route.Key;
}
