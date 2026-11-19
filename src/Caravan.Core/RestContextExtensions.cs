using Caravan.Model;
using Caravan.Model.Rest;

namespace Caravan;

/// <summary>The REST DSL state of a context.</summary>
public static class RestContextExtensions
{
    /// <summary>The context's REST configuration (created with defaults on first use).</summary>
    /// <param name="context">The context.</param>
    /// <returns>The configuration, shared by every REST service of the context.</returns>
    public static RestConfiguration GetRestConfiguration(this ICaravanContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.GetOrAddExtension(() => new RestConfiguration());
    }

    /// <summary>
    /// The REST services of the context: the route definitions created from REST definitions, with their verb (in route order). Removing
    /// a route removes its service.
    /// </summary>
    /// <param name="context">The context.</param>
    /// <returns>The services.</returns>
    public static IReadOnlyList<(RouteDefinition Route, VerbDefinition Verb)> GetRestServices(this ICaravanContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return [.. context.RouteDefinitions
            .Where(r => r.Extensions.ContainsKey(typeof(VerbDefinition)))
            .Select(r => (r, (VerbDefinition)r.Extensions[typeof(VerbDefinition)]))];
    }
}
