using Caravan.LoadBalancing;
using Caravan.Spi;

namespace Caravan.Model;

/// <summary>Random load balancing; see <see cref="RandomLoadBalancer"/>.</summary>
public sealed class RandomLoadBalancerDefinition : LoadBalancerDefinition
{
    /// <inheritdoc />
    public override string Name => "random";

    /// <inheritdoc />
    public override ILoadBalancer CreateLoadBalancer(RouteContext routeContext, int processorCount) => new RandomLoadBalancer();
}
