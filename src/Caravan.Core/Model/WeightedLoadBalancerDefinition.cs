using Caravan.LoadBalancing;
using Caravan.Spi;

namespace Caravan.Model;

/// <summary>Weighted load balancing; see <see cref="WeightedLoadBalancer"/>.</summary>
public sealed class WeightedLoadBalancerDefinition : LoadBalancerDefinition
{
    /// <summary>Whether processors are chosen in round-robin order (else at random).</summary>
    public bool RoundRobin { get; set; }

    /// <summary>The weight of each processor, in order (Camel's <c>distributionRatio</c>); one per processor.</summary>
    public List<int> DistributionRatio { get; } = [];

    /// <inheritdoc />
    public override string Name => "weighted";

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">The number of weights does not match the number of processors, or a weight is invalid.</exception>
    public override ILoadBalancer CreateLoadBalancer(RouteContext routeContext, int processorCount)
    {
        if (DistributionRatio.Count != processorCount)
        {
            throw new InvalidOperationException(
                $"Load balancing with {DistributionRatio.Count} weights ({string.Join(",", DistributionRatio)}) does not match the number of processors: {processorCount}");
        }

        try
        {
            return new WeightedLoadBalancer(DistributionRatio, RoundRobin);
        }
        catch (ArgumentException e)
        {
            throw new InvalidOperationException(e.Message, e);
        }
    }
}
