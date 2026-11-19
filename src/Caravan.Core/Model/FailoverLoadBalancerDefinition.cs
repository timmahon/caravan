using Caravan.LoadBalancing;
using Caravan.Spi;

namespace Caravan.Model;

/// <summary>Failover load balancing; see <see cref="FailoverLoadBalancer"/>.</summary>
public sealed class FailoverLoadBalancerDefinition : LoadBalancerDefinition
{
    /// <summary>The maximum number of failovers: -1 (default) for unbounded, 0 for none.</summary>
    public int MaximumFailoverAttempts { get; set; } = -1;

    /// <summary>Whether the outputs keep the route's error handler (default true), so each is redelivered before failing over.</summary>
    public bool InheritErrorHandlerEnabled { get; set; } = true;

    /// <summary>Whether exchanges start with the next processor in turn and failover wraps around.</summary>
    public bool RoundRobin { get; set; }

    /// <summary>Whether exchanges start with the processor that last succeeded.</summary>
    public bool Sticky { get; set; }

    /// <summary>The exceptions to fail over on; empty for any.</summary>
    public List<Type> Exceptions { get; } = [];

    /// <inheritdoc />
    public override string Name => "failover";

    /// <inheritdoc />
    public override bool InheritErrorHandler => InheritErrorHandlerEnabled;

    /// <inheritdoc />
    public override ILoadBalancer CreateLoadBalancer(RouteContext routeContext, int processorCount) =>
        new FailoverLoadBalancer(MaximumFailoverAttempts, RoundRobin, Sticky, Exceptions);
}
