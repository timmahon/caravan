using Caravan.Model;

namespace Caravan.Builder;

/// <summary>DSL for the <c>sample</c> EIP.</summary>
public static class SampleDsl
{
    /// <summary>
    /// Lets one exchange through per <paramref name="samplePeriod"/> (default 1 second, on the context's clock) and stops the others:
    /// <c>.Sample(TimeSpan.FromSeconds(5)).To("mock:sampled")</c>.
    /// </summary>
    /// <param name="steps">The block to add to.</param>
    /// <param name="samplePeriod">The period; null for 1 second.</param>
    public static T Sample<T>(this T steps, TimeSpan? samplePeriod = null)
        where T : RouteSteps
    {
        steps.AddStep(new SampleDefinition { SamplePeriod = samplePeriod ?? TimeSpan.FromSeconds(1) });
        return steps;
    }

    /// <summary>Lets every <paramref name="messageFrequency"/>th exchange through and stops the others: <c>.Sample(messageFrequency: 10)</c>.</summary>
    /// <param name="steps">The block to add to.</param>
    /// <param name="messageFrequency">The frequency.</param>
    public static T Sample<T>(this T steps, long messageFrequency)
        where T : RouteSteps
    {
        steps.AddStep(new SampleDefinition { MessageFrequency = messageFrequency });
        return steps;
    }
}
