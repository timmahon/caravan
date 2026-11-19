using Caravan.Model;

namespace Caravan.Builder;

/// <summary>
/// Fluent builder for a block of steps: a route body or a nested block (choice branch, split body, ...).
/// EIPs are added through extension methods so features can live in separate files and packages.
/// Extension methods are generic over <c>T : RouteSteps</c> so chaining keeps the concrete builder type.
/// </summary>
public class RouteSteps
{
    /// <summary>Creates a builder adding steps to <paramref name="outputs"/>.</summary>
    /// <param name="outputs">The list that receives the step definitions.</param>
    /// <param name="route">The owning route, if any.</param>
    /// <param name="parent">The definition owning this block, or null for a route's top-level block.</param>
    public RouteSteps(List<ProcessorDefinition> outputs, RouteDefinition? route = null, ProcessorDefinition? parent = null)
    {
        Outputs = outputs;
        Route = route;
        Parent = parent;
    }

    /// <summary>The steps being built.</summary>
    public List<ProcessorDefinition> Outputs { get; }

    /// <summary>The route this block belongs to (null for blocks built outside a route).</summary>
    public RouteDefinition? Route { get; }

    /// <summary>The definition owning this block, or null for the route's top-level block.</summary>
    public ProcessorDefinition? Parent { get; }

    /// <summary>The most recently added step.</summary>
    public ProcessorDefinition? Last => Outputs.Count > 0 ? Outputs[^1] : null;

    /// <summary>Adds a step definition.</summary>
    public TDefinition AddStep<TDefinition>(TDefinition definition)
        where TDefinition : ProcessorDefinition
    {
        definition.Parent = Parent;
        definition.Route = Route;
        Outputs.Add(definition);
        return definition;
    }

    /// <summary>Creates a builder for a nested block.</summary>
    public RouteSteps Nested(List<ProcessorDefinition> outputs, ProcessorDefinition parent) => new(outputs, Route, parent);

    /// <summary>Builds a nested block with <paramref name="configure"/>.</summary>
    public void BuildNested(List<ProcessorDefinition> outputs, ProcessorDefinition parent, Action<RouteSteps>? configure)
    {
        configure?.Invoke(Nested(outputs, parent));
    }
}
