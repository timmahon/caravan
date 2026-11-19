namespace Caravan.Model;

/// <summary>A parameter of a route template (Camel's <c>templateParameter</c>), referenced in the template as <c>{{name}}</c>.</summary>
public sealed class RouteTemplateParameterDefinition
{
    /// <summary>Creates a parameter.</summary>
    /// <param name="name">The parameter name.</param>
    public RouteTemplateParameterDefinition(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
    }

    /// <summary>The parameter name.</summary>
    public string Name { get; }

    /// <summary>The value used when the parameter is not given, or null.</summary>
    public string? DefaultValue { get; set; }

    /// <summary>A description of the parameter.</summary>
    public string? Description { get; set; }

    /// <summary>
    /// Whether a value must be given (default true). A parameter with a <see cref="DefaultValue"/> is never missing; an optional parameter
    /// without value is left to the context's properties (and <c>{{?name}}</c> drops the URI option using it).
    /// </summary>
    public bool Required { get; set; } = true;

    /// <inheritdoc />
    public override string ToString() => DefaultValue is null ? Name : $"{Name}={DefaultValue}";
}
