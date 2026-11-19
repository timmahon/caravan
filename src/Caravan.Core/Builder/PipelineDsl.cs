using Caravan.Model;

namespace Caravan.Builder;

/// <summary>DSL for the <c>pipeline</c> and <c>step</c> EIPs.</summary>
public static class PipelineDsl
{
    /// <summary>Adds an explicit nested block of steps run in sequence.</summary>
    public static T Pipeline<T>(this T steps, Action<RouteSteps> block)
        where T : RouteSteps
    {
        var definition = steps.AddStep(new PipelineDefinition());
        steps.BuildNested(definition.Outputs, definition, block);
        return steps;
    }

    /// <summary>
    /// Adds a named block of steps. While inside, the exchange property
    /// <see cref="ExchangePropertyKeys.StepId"/> is <paramref name="id"/>.
    /// </summary>
    public static T Step<T>(this T steps, string id, Action<RouteSteps> block)
        where T : RouteSteps
    {
        var definition = steps.AddStep(new StepDefinition { Id = id });
        steps.BuildNested(definition.Outputs, definition, block);
        return steps;
    }

    /// <summary>Adds a block of steps with a generated step id.</summary>
    public static T Step<T>(this T steps, Action<RouteSteps> block)
        where T : RouteSteps
    {
        var definition = steps.AddStep(new StepDefinition());
        steps.BuildNested(definition.Outputs, definition, block);
        return steps;
    }
}
