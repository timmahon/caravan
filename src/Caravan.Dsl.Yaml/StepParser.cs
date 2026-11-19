using Caravan.Dsl.Yaml.Catalog;
using Caravan.Model;
using YamlDotNet.RepresentationModel;

namespace Caravan.Dsl.Yaml;

/// <summary>Reads lists of steps (<c>steps: [ { to: ... }, { setBody: ... } ]</c>) into processor definitions using the <see cref="StepCatalog"/>.</summary>
internal static class StepParser
{
    /// <summary>Reads the steps of <paramref name="node"/> into <paramref name="outputs"/>.</summary>
    /// <param name="context">The load context.</param>
    /// <param name="node">A sequence of steps (null means none).</param>
    /// <param name="outputs">The list to add to.</param>
    /// <param name="route">The route when these are its top-level steps (route-scoped <c>onException</c>/<c>onCompletion</c> go there).</param>
    public static void ParseSteps(LoadContext context, YamlNode node, List<ProcessorDefinition> outputs, RouteDefinition? route = null)
    {
        if (YamlObject.IsNull(node))
        {
            return;
        }

        if (node is not YamlSequenceNode sequence)
        {
            throw context.Error(node, "'steps' must be a list of steps, e.g. steps: [ { to: \"mock:a\" } ]");
        }

        foreach (var item in sequence.Children)
        {
            if (ParseStep(context, item, route) is { } definition)
            {
                outputs.Add(definition);
            }
        }
    }

    /// <summary>Reads one step (<c>{ setHeader: { ... } }</c>); null when it was added to the route (route-scoped clauses).</summary>
    public static ProcessorDefinition? ParseStep(LoadContext context, YamlNode item, RouteDefinition? route)
    {
        if (item is not YamlMappingNode mapping || mapping.Children.Count != 1)
        {
            var keys = item is YamlMappingNode m ? string.Join(", ", m.Children.Keys) : "none";
            throw context.Error(item, $"A step must be an object with exactly one key, the step name (found: {keys})");
        }

        var (keyNode, value) = mapping.Children.First();
        if (keyNode is not YamlScalarNode { Value: { Length: > 0 } rawName })
        {
            throw context.Error(keyNode, "A step name must be a string");
        }

        var name = NameConverter.ToCamelCase(rawName);
        if (!StepCatalog.Steps.TryGetValue(name, out var spec))
        {
            if (StepCatalog.Unsupported.TryGetValue(name, out var why))
            {
                throw context.Error(keyNode, $"Step '{rawName}' is not supported by Caravan: {why}");
            }

            var hint = NameConverter.Closest(name, StepCatalog.Steps.Keys) is { } closest ? $" Did you mean '{closest}'?" : string.Empty;
            throw context.Error(keyNode, $"Unknown step '{rawName}'.{hint}");
        }

        var options = new YamlObject(context, spec.Name, value, spec);
        ProcessorDefinition definition;
        try
        {
            definition = spec.Build(options);
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or FormatException or CaravanException
            && e is not YamlRouteDefinitionException)
        {
            throw context.Error(keyNode, $"Invalid '{spec.Name}': {e.Message}", e);
        }

        options.ApplyCommon(definition);
        if (!spec.RouteLevelOnly)
        {
            return definition;
        }

        switch (definition)
        {
            case OnExceptionDefinition onException when route is not null:
                onException.RouteScoped = true;
                route.OnExceptions.Add(onException);
                return null;
            case OnCompletionDefinition onCompletion when route is not null:
                onCompletion.RouteScoped = true;
                route.OnCompletions.Add(onCompletion);
                return null;
            default:
                throw context.Error(
                    keyNode,
                    $"'{spec.Name}' is only allowed among the top-level steps of a route, at the top level of the file or in a routeConfiguration");
        }
    }
}
