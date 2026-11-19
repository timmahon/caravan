using Caravan.Spi;
using Caravan.Support;

namespace Caravan.Model;

/// <summary>A custom <see cref="ILoadBalancer"/>, given directly or by registry name.</summary>
public sealed class CustomLoadBalancerDefinition : LoadBalancerDefinition
{
    /// <summary>The load balancer.</summary>
    public ILoadBalancer? LoadBalancer { get; set; }

    /// <summary>The name of an <see cref="ILoadBalancer"/> in the registry, used when <see cref="LoadBalancer"/> is null.</summary>
    public string? Ref { get; set; }

    /// <inheritdoc />
    public override string Name => Ref is null ? "custom" : $"custom[ref:{Ref}]";

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">No load balancer is configured.</exception>
    public override ILoadBalancer CreateLoadBalancer(RouteContext routeContext, int processorCount) =>
        LoadBalancer ?? RegistryLookup.Lookup<ILoadBalancer>(routeContext.Context, Ref)
        ?? throw new InvalidOperationException("customLoadBalancer: a load balancer or ref must be configured");
}
