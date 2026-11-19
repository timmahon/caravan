using Caravan.Processors;
using Caravan.Spi;

namespace Caravan.Model;

/// <summary>
/// The <c>loadBalance</c> EIP: each step of the block is an output, and the <see cref="LoadBalancer"/> decides which output(s) process each
/// exchange; see <see cref="LoadBalanceProcessor"/>.
/// </summary>
public sealed class LoadBalanceDefinition : OutputDefinition
{
    /// <summary>The load balancer.</summary>
    public LoadBalancerDefinition? LoadBalancer { get; set; }

    /// <inheritdoc />
    public override string ShortName => "loadBalance";

    /// <inheritdoc />
    public override string Label => $"loadBalance[{LoadBalancer}]";

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">No load balancer is configured, or it does not fit the outputs.</exception>
    public override IProcessor CreateProcessor(RouteContext routeContext)
    {
        var loadBalancer = LoadBalancer ?? throw new InvalidOperationException("loadBalance: a load balancer must be configured");
        var errorHandler = routeContext.ErrorHandlerFactory;
        if (!loadBalancer.InheritErrorHandler)
        {
            routeContext.ErrorHandlerFactory = null;
        }

        var processors = new List<IProcessor>();
        try
        {
            foreach (var output in Outputs)
            {
                output.Parent = this;
                if (routeContext.CreateNodeProcessor(output) is { } processor)
                {
                    processors.Add(processor);
                }
            }
        }
        finally
        {
            routeContext.ErrorHandlerFactory = errorHandler;
        }

        return new LoadBalanceProcessor(loadBalancer.CreateLoadBalancer(routeContext, processors.Count), processors);
    }
}
