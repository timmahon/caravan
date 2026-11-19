using System.Text;
using Caravan.Model;
using Caravan.Model.Rest;
using Caravan.Support;

namespace Caravan.Components.Rest;

/// <summary>Turns REST definitions into routes (Camel's <c>RestDefinition.asRouteDefinition</c>), one per verb.</summary>
internal static class RestRouteFactory
{
    /// <summary>Creates the routes of <paramref name="rests"/> and inlines <c>direct</c> routes of <paramref name="routes"/> when configured.</summary>
    /// <param name="rests">The REST definitions.</param>
    /// <param name="routes">The routes of the same builder; the created routes are added, inlined routes removed.</param>
    /// <param name="inlineRoutes">Whether to inline <c>direct</c> routes.</param>
    public static void AddRoutes(IReadOnlyList<RestDefinition> rests, List<RouteDefinition> routes, bool inlineRoutes)
    {
        var created = new List<(RouteDefinition Route, RestBindingDefinition Binding, VerbDefinition Verb)>();
        foreach (var rest in rests.Where(r => !r.Disabled))
        {
            foreach (var verb in rest.Verbs.Where(v => !v.Disabled))
            {
                created.Add(CreateRoute(rest, verb));
            }
        }

        if (inlineRoutes)
        {
            Inline(created, routes);
        }

        routes.AddRange(created.Select(c => c.Route));
    }

    /// <summary>The base path of a REST definition: a leading slash, no trailing slash.</summary>
    public static string NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || path == "/")
        {
            return string.Empty;
        }

        path = path.Trim();
        return (path.StartsWith('/') ? path : "/" + path).TrimEnd('/');
    }

    private static (RouteDefinition, RestBindingDefinition, VerbDefinition) CreateRoute(RestDefinition rest, VerbDefinition verb)
    {
        verb.Rest ??= rest;
        var basePath = NormalizePath(rest.Path);
        var template = NormalizePath(verb.Path);
        if (basePath.Length == 0 && template.Length == 0)
        {
            basePath = "/";
        }

        var consumes = verb.Consumes ?? rest.Consumes;
        var produces = verb.Produces ?? rest.Produces;
        var uri = new StringBuilder("rest:").Append(verb.Method.ToLowerInvariant()).Append(':').Append(basePath);
        if (template.Length > 0)
        {
            uri.Append(':').Append(template);
        }

        var options = new List<string>();
        if (consumes is not null)
        {
            options.Add("consumes=" + Escape(consumes));
        }

        if (produces is not null)
        {
            options.Add("produces=" + Escape(produces));
        }

        if ((verb.EnableCors ?? rest.EnableCors) is { } cors)
        {
            options.Add("enableCors=" + (cors ? "true" : "false"));
        }

        if (options.Count > 0)
        {
            uri.Append('?').AppendJoin('&', options);
        }

        var route = new RouteDefinition(uri.ToString())
        {
            Id = verb.RouteId ?? verb.Id,
            Description = verb.Description,
        };
        route.Extensions[typeof(VerbDefinition)] = verb;
        var binding = new RestBindingDefinition
        {
            Method = verb.Method.ToUpperInvariant(),
            Consumes = consumes,
            Produces = produces,
            BindingMode = verb.BindingMode ?? rest.BindingMode,
            Type = verb.Type,
            OutType = verb.OutType,
            SkipBindingOnErrorCode = verb.SkipBindingOnErrorCode ?? rest.SkipBindingOnErrorCode,
            ClientRequestValidation = verb.ClientRequestValidation ?? rest.ClientRequestValidation,
            EnableCors = verb.EnableCors ?? rest.EnableCors,
            Route = route,
        };
        binding.Params.AddRange(verb.Params);
        route.Outputs.Add(binding);
        if (verb.Outputs.Count > 0)
        {
            if (verb.To is not null)
            {
                throw new ArgumentException($"REST service {verb} has both a to and an inline route: use one");
            }

            Adopt(binding, verb.Outputs, route);
        }
        else if (verb.To is not null)
        {
            Adopt(binding, [new ToDefinition(verb.To)], route);
        }
        else
        {
            throw new ArgumentException($"REST service {verb} has neither a to nor an inline route");
        }

        return (route, binding, verb);
    }

    private static void Inline(List<(RouteDefinition Route, RestBindingDefinition Binding, VerbDefinition Verb)> created, List<RouteDefinition> routes)
    {
        var targets = created
            .Where(c => c.Binding.Outputs is [ToDefinition to] && to.Uri.StartsWith("direct:", StringComparison.Ordinal))
            .GroupBy(c => Key(((ToDefinition)c.Binding.Outputs[0]).Uri))
            .Where(g => g.Count() == 1);
        foreach (var group in targets)
        {
            var (route, binding, _) = group.Single();
            var direct = routes.FirstOrDefault(r => Key(r.FromUri) == group.Key);
            if (direct is null)
            {
                continue;
            }

            routes.Remove(direct);
            binding.Outputs.Clear();
            Adopt(binding, direct.Outputs, route);
            route.Id ??= direct.Id;
            route.Description ??= direct.Description;
            route.Group = direct.Group;
            route.AutoStartup = direct.AutoStartup;
            route.StartupOrder = direct.StartupOrder;
            route.StreamCaching = direct.StreamCaching;
            route.ErrorHandler = direct.ErrorHandler;
            route.RouteConfigurationIds = direct.RouteConfigurationIds;
            route.OnExceptions.AddRange(direct.OnExceptions);
            route.OnCompletions.AddRange(direct.OnCompletions);
            route.Intercepts.AddRange(direct.Intercepts);
            route.InterceptFroms.AddRange(direct.InterceptFroms);
            route.InterceptSendToEndpoints.AddRange(direct.InterceptSendToEndpoints);
            foreach (var (key, value) in direct.Properties)
            {
                route.Properties[key] = value;
            }
        }
    }

    private static string Key(string uri)
    {
        var parsed = EndpointUri.Parse(uri);
        return parsed.Scheme + ":" + parsed.Path;
    }

    private static void Adopt(RestBindingDefinition binding, IEnumerable<ProcessorDefinition> outputs, RouteDefinition route)
    {
        foreach (var output in outputs.ToList())
        {
            output.Parent = binding;
            SetRoute(output, route);
            binding.Outputs.Add(output);
        }
    }

    private static void SetRoute(ProcessorDefinition node, RouteDefinition route)
    {
        node.Route = route;
        foreach (var child in node.Children)
        {
            SetRoute(child, route);
        }
    }

    private static string Escape(string value) =>
        value.Replace("%", "%25", StringComparison.Ordinal).Replace("&", "%26", StringComparison.Ordinal)
            .Replace("+", "%2B", StringComparison.Ordinal).Replace(" ", "%20", StringComparison.Ordinal);
}
