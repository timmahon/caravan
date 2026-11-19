using Caravan.Model;

namespace Caravan.Builder;

/// <summary>
/// Fluent builder for a block of steps: a route body or a nested block (choice branch, split body, ...).
/// EIPs are added through extension methods so features can live in separate files and packages.
/// Extension methods are generic over <c>T : RouteSteps</c> so chaining keeps the concrete builder type.
/// </summary>
public class RouteSteps
{
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

/// <summary>Builder for a route: adds route-level options to <see cref="RouteSteps"/>.</summary>
public sealed class RouteDefinitionBuilder : RouteSteps
{
    public RouteDefinitionBuilder(RouteDefinition definition)
        : base(definition.Outputs, definition)
    {
        Definition = definition;
    }

    public RouteDefinition Definition { get; }

    public RouteDefinitionBuilder RouteId(string id)
    {
        Definition.Id = id;
        return this;
    }

    public RouteDefinitionBuilder RouteDescription(string description)
    {
        Definition.Description = description;
        return this;
    }

    public RouteDefinitionBuilder RouteGroup(string group)
    {
        Definition.Group = group;
        return this;
    }

    public RouteDefinitionBuilder AutoStartup(bool autoStartup)
    {
        Definition.AutoStartup = autoStartup;
        return this;
    }

    public RouteDefinitionBuilder StartupOrder(int order)
    {
        Definition.StartupOrder = order;
        return this;
    }

    public RouteDefinitionBuilder RouteProperty(string key, object? value)
    {
        Definition.Properties[key] = value;
        return this;
    }
}
