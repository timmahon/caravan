using Caravan.Model;

namespace Caravan.Builder;

/// <summary>DSL for the <c>stop</c> EIP.</summary>
public static class StopDsl
{
    /// <summary>Stops routing the exchange: no further steps run, in this route or in the routes that called it.</summary>
    public static T Stop<T>(this T steps)
        where T : RouteSteps
    {
        steps.AddStep(new StopDefinition());
        return steps;
    }
}
