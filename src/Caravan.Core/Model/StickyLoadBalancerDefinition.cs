using Caravan.LoadBalancing;
using Caravan.Spi;

namespace Caravan.Model;

/// <summary>Sticky load balancing by correlation value; see <see cref="StickyLoadBalancer"/>.</summary>
public sealed class StickyLoadBalancerDefinition(IExpression correlationExpression) : LoadBalancerDefinition
{
    /// <summary>Computes the correlation value.</summary>
    public IExpression CorrelationExpression { get; set; } = correlationExpression;

    /// <inheritdoc />
    public override string Name => "sticky";

    /// <inheritdoc />
    public override ILoadBalancer CreateLoadBalancer(RouteContext routeContext, int processorCount) => new StickyLoadBalancer(CorrelationExpression);
}
