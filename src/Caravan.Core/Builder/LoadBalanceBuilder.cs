using Caravan.Model;
using Caravan.Spi;

namespace Caravan.Builder;

/// <summary>Chooses the load balancer of the <c>loadBalance</c> EIP: <c>.LoadBalance(lb =&gt; lb.RoundRobin(), o =&gt; o.To("mock:a").To("mock:b"))</c>.</summary>
public sealed class LoadBalanceBuilder
{
    /// <summary>Creates the builder for <paramref name="definition"/>.</summary>
    /// <param name="definition">The definition being configured.</param>
    public LoadBalanceBuilder(LoadBalanceDefinition definition)
    {
        Definition = definition;
    }

    /// <summary>The definition being configured.</summary>
    public LoadBalanceDefinition Definition { get; }

    /// <summary>Sends exchanges to the outputs in turn.</summary>
    public LoadBalanceBuilder RoundRobin() => Use(new RoundRobinLoadBalancerDefinition());

    /// <summary>Sends each exchange to an output chosen at random.</summary>
    public LoadBalanceBuilder Random() => Use(new RandomLoadBalancerDefinition());

    /// <summary>Sends a copy of each exchange to every output, in order (stopping at the first failure).</summary>
    public LoadBalanceBuilder Topic() => Use(new TopicLoadBalancerDefinition());

    /// <summary>
    /// Distributes exchanges in proportion to <paramref name="weights"/> (one per output): with weights 4, 2, 1 each cycle of 7 exchanges sends
    /// 4 to the first output, 2 to the second and 1 to the third.
    /// </summary>
    /// <param name="roundRobin">Choose in round-robin order (else at random within each cycle).</param>
    /// <param name="weights">The weight of each output.</param>
    public LoadBalanceBuilder Weighted(bool roundRobin, params int[] weights)
    {
        var definition = new WeightedLoadBalancerDefinition { RoundRobin = roundRobin };
        definition.DistributionRatio.AddRange(weights);
        return Use(definition);
    }

    /// <summary>Distributes exchanges in proportion to the weights in <paramref name="distributionRatio"/> (e.g. <c>"4,2,1"</c>), one per output.</summary>
    /// <param name="roundRobin">Choose in round-robin order (else at random within each cycle).</param>
    /// <param name="distributionRatio">The weights, separated by <paramref name="distributionRatioDelimiter"/>.</param>
    /// <param name="distributionRatioDelimiter">The delimiter (default <c>,</c>).</param>
    public LoadBalanceBuilder Weighted(bool roundRobin, string distributionRatio, string distributionRatioDelimiter = ",") =>
        Weighted(roundRobin, distributionRatio
            .Split(distributionRatioDelimiter, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(w => int.Parse(w, System.Globalization.CultureInfo.InvariantCulture))
            .ToArray());

    /// <summary>
    /// Fails over to the next output when an output fails (with any exception). Every exchange starts with the first output, and failover stops
    /// after the last one unless <paramref name="roundRobin"/>.
    /// </summary>
    /// <param name="maximumFailoverAttempts">The maximum number of failovers: -1 (default) for unbounded, 0 for none.</param>
    /// <param name="inheritErrorHandler">Keep the route's error handler on the outputs (default), so each is redelivered before failing over.</param>
    /// <param name="roundRobin">Start each exchange with the next output in turn, wrapping around on failover.</param>
    /// <param name="sticky">Start each exchange with the output that last succeeded.</param>
    /// <param name="exceptions">Fail over only on these exceptions (searched in the cause chain); null for any.</param>
    public LoadBalanceBuilder Failover(int maximumFailoverAttempts = -1, bool inheritErrorHandler = true, bool roundRobin = false, bool sticky = false, IEnumerable<Type>? exceptions = null)
    {
        var definition = new FailoverLoadBalancerDefinition
        {
            MaximumFailoverAttempts = maximumFailoverAttempts,
            InheritErrorHandlerEnabled = inheritErrorHandler,
            RoundRobin = roundRobin,
            Sticky = sticky,
        };
        definition.Exceptions.AddRange(exceptions ?? []);
        return Use(definition);
    }

    /// <summary>Fails over to the next output when an output fails with one of <paramref name="exceptions"/>: <c>lb.Failover(typeof(IOException))</c>.</summary>
    /// <param name="exceptions">The exceptions to fail over on (searched in the cause chain).</param>
    public LoadBalanceBuilder Failover(params Type[] exceptions) => Failover(exceptions: (IEnumerable<Type>)exceptions);

    /// <summary>Sends exchanges with the same value of <paramref name="correlationExpression"/> to the same output.</summary>
    /// <param name="correlationExpression">Computes the correlation value.</param>
    public LoadBalanceBuilder Sticky(IExpression correlationExpression) => Use(new StickyLoadBalancerDefinition(correlationExpression));

    /// <summary>Sends exchanges with the same value of <paramref name="correlationExpression"/> to the same output.</summary>
    /// <param name="correlationExpression">Computes the correlation value.</param>
    public LoadBalanceBuilder Sticky(Func<Exchange, object?> correlationExpression) => Sticky(new FuncExpression(correlationExpression));

    /// <summary>Uses a custom load balancer.</summary>
    /// <param name="loadBalancer">The load balancer.</param>
    public LoadBalanceBuilder Custom(ILoadBalancer loadBalancer) => Use(new CustomLoadBalancerDefinition { LoadBalancer = loadBalancer });

    /// <summary>Uses the <see cref="ILoadBalancer"/> bound under <paramref name="name"/> in the registry.</summary>
    /// <param name="name">The registry name.</param>
    public LoadBalanceBuilder Custom(string name) => Use(new CustomLoadBalancerDefinition { Ref = name });

    private LoadBalanceBuilder Use(LoadBalancerDefinition loadBalancer)
    {
        Definition.LoadBalancer = loadBalancer;
        return this;
    }
}
