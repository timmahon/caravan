using Caravan.Builder;
using Caravan.Model;

namespace Caravan.Dsl.Yaml;

/// <summary>Builds definitions with the C# DSL (so the YAML loader reuses its validation and factories rather than duplicating them).</summary>
internal static class Dsl
{
    /// <summary>The single definition <paramref name="build"/> adds.</summary>
    public static ProcessorDefinition Single(Action<RouteSteps> build)
    {
        var outputs = new List<ProcessorDefinition>();
        build(new RouteSteps(outputs));
        return outputs.Count == 1 ? outputs[0] : throw new InvalidOperationException("Expected one step");
    }
}
