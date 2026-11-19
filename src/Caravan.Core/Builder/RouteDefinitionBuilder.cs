using Caravan.Model;

namespace Caravan.Builder;

/// <summary>Builder for a route: adds route-level options to <see cref="RouteSteps"/>.</summary>
public sealed class RouteDefinitionBuilder : RouteSteps
{
    /// <summary>Creates a builder for <paramref name="definition"/>.</summary>
    public RouteDefinitionBuilder(RouteDefinition definition)
        : base(definition.Outputs, definition)
    {
        Definition = definition;
    }

    /// <summary>The route being built.</summary>
    public RouteDefinition Definition { get; }

    /// <summary>Sets the route id (must be unique in the context).</summary>
    public RouteDefinitionBuilder RouteId(string id)
    {
        Definition.Id = id;
        return this;
    }

    /// <summary>Sets a human-readable description of the route.</summary>
    public RouteDefinitionBuilder RouteDescription(string description)
    {
        Definition.Description = description;
        return this;
    }

    /// <summary>Puts the route in a named group.</summary>
    public RouteDefinitionBuilder RouteGroup(string group)
    {
        Definition.Group = group;
        return this;
    }

    /// <summary>Whether the route starts with the context (default true).</summary>
    public RouteDefinitionBuilder AutoStartup(bool autoStartup)
    {
        Definition.AutoStartup = autoStartup;
        return this;
    }

    /// <summary>Routes with a lower order start first and stop last.</summary>
    public RouteDefinitionBuilder StartupOrder(int order)
    {
        Definition.StartupOrder = order;
        return this;
    }

    /// <summary>Sets an arbitrary route property.</summary>
    public RouteDefinitionBuilder RouteProperty(string key, object? value)
    {
        Definition.Properties[key] = value;
        return this;
    }
}
