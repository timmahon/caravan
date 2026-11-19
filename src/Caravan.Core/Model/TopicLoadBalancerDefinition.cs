using Caravan.LoadBalancing;
using Caravan.Spi;

namespace Caravan.Model;

/// <summary>Topic load balancing: every processor gets a copy; see <see cref="TopicLoadBalancer"/>.</summary>
public sealed class TopicLoadBalancerDefinition : LoadBalancerDefinition
{
    /// <inheritdoc />
    public override string Name => "topic";

    /// <inheritdoc />
    public override ILoadBalancer CreateLoadBalancer(RouteContext routeContext, int processorCount) => new TopicLoadBalancer();
}
