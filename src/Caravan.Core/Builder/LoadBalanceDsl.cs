using Caravan.Model;

namespace Caravan.Builder;

/// <summary>DSL for the <c>loadBalance</c> EIP.</summary>
public static class LoadBalanceDsl
{
    /// <summary>
    /// Balances exchanges over the steps of <paramref name="outputs"/> (each step is an output; use <c>Pipeline</c> to group steps) with the
    /// load balancer chosen by <paramref name="configure"/>: <c>.LoadBalance(lb =&gt; lb.Failover(typeof(IOException)), o =&gt; o.To("direct:a").To("direct:b"))</c>.
    /// </summary>
    /// <param name="steps">The block to add to.</param>
    /// <param name="configure">Chooses the load balancer.</param>
    /// <param name="outputs">The outputs.</param>
    public static T LoadBalance<T>(this T steps, Action<LoadBalanceBuilder> configure, Action<RouteSteps> outputs)
        where T : RouteSteps
    {
        ArgumentNullException.ThrowIfNull(configure);
        var definition = steps.AddStep(new LoadBalanceDefinition());
        configure(new LoadBalanceBuilder(definition));
        steps.BuildNested(definition.Outputs, definition, outputs);
        return steps;
    }
}
