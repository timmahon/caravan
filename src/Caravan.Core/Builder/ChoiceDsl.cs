using Caravan.Model;

namespace Caravan.Builder;

/// <summary>DSL for the <c>choice</c> EIP (content-based router).</summary>
public static class ChoiceDsl
{
    /// <summary>
    /// Routes each exchange to the first matching <c>When</c> block, or the <c>Otherwise</c> block; routing continues
    /// after the choice: <c>.Choice(c =&gt; c.When(p, r =&gt; r.To("mock:a")).Otherwise(r =&gt; r.To("mock:b")))</c>.
    /// </summary>
    public static T Choice<T>(this T steps, Action<ChoiceBuilder> configure)
        where T : RouteSteps
    {
        var definition = steps.AddStep(new ChoiceDefinition());
        configure(new ChoiceBuilder(steps, definition));
        return steps;
    }
}
