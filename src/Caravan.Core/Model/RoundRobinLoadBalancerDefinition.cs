using Caravan.LoadBalancing;
using Caravan.Spi;

namespace Caravan.Model;

/// <summary>Round-robin load balancing; see <see cref="RoundRobinLoadBalancer"/>.</summary>
public sealed class RoundRobinLoadBalancerDefinition : LoadBalancerDefinition
{
    /// <inheritdoc />
    public override string Name => "roundRobin";

    /// <inheritdoc />
    public override ILoadBalancer CreateLoadBalancer(RouteContext routeContext, int processorCount) => new RoundRobinLoadBalancer();
}
