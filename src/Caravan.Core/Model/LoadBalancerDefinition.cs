using Caravan.Spi;

namespace Caravan.Model;

/// <summary>The load balancer of a <see cref="LoadBalanceDefinition"/> (Camel's <c>LoadBalancerDefinition</c>).</summary>
public abstract class LoadBalancerDefinition
{
    /// <summary>The load balancer's name, e.g. <c>roundRobin</c>.</summary>
    public abstract string Name { get; }

    /// <summary>Whether the outputs are wrapped with the route's error handler (true except for a failover that disables it).</summary>
    public virtual bool InheritErrorHandler => true;

    /// <summary>Creates the load balancer.</summary>
    /// <param name="routeContext">The route being built.</param>
    /// <param name="processorCount">The number of processors it balances over.</param>
    /// <returns>The load balancer.</returns>
    public abstract ILoadBalancer CreateLoadBalancer(RouteContext routeContext, int processorCount);

    /// <inheritdoc />
    public override string ToString() => Name;
}
