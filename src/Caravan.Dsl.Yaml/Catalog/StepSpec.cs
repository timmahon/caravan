using Caravan.Model;

namespace Caravan.Dsl.Yaml.Catalog;

/// <summary>A step (EIP) of the YAML DSL: its name, options and how its definition is built.</summary>
internal sealed class StepSpec : ObjectSpec
{
    /// <summary>Creates the spec; steps take the common options (<c>id</c>, <c>description</c>, <c>disabled</c>).</summary>
    public StepSpec()
    {
        CommonOptions = true;
    }

    /// <summary>The camelCase step name (<c>setHeader</c>).</summary>
    public required string Name { get; init; }

    /// <summary>What the step does.</summary>
    public required string Description { get; init; }

    /// <summary>Builds the definition from the validated options.</summary>
    public required Func<YamlObject, ProcessorDefinition> Build { get; init; }

    /// <summary>Whether the step is only allowed among the top-level steps of a route (route-scoped <c>onException</c>, <c>onCompletion</c>).</summary>
    public bool RouteLevelOnly { get; init; }
}
