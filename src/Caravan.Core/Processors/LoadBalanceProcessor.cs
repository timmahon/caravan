using Caravan.Spi;
using Caravan.Support;

namespace Caravan.Processors;

/// <summary>The <c>loadBalance</c> EIP: hands each exchange to the <see cref="LoadBalancer"/>, which picks among <see cref="Processors"/>.</summary>
public sealed class LoadBalanceProcessor(ILoadBalancer loadBalancer, IReadOnlyList<IProcessor> processors) : ServiceSupport, INavigableProcessor
{
    /// <summary>Chooses the processors of each exchange.</summary>
    public ILoadBalancer LoadBalancer { get; } = loadBalancer;

    /// <summary>The processors balanced over (the node's outputs).</summary>
    public IReadOnlyList<IProcessor> Processors { get; } = processors;

    /// <inheritdoc />
    public IReadOnlyList<IProcessor> Next => Processors;

    /// <inheritdoc />
    public ValueTask ProcessAsync(Exchange exchange) => LoadBalancer.ProcessAsync(exchange, Processors);

    /// <inheritdoc />
    public override string ToString() => $"LoadBalance[{LoadBalancer}: {string.Join(", ", Processors)}]";

    /// <summary>Starts the load balancer when it is a service.</summary>
    protected override Task DoStartAsync(CancellationToken cancellationToken) => StartServiceAsync(LoadBalancer, cancellationToken);

    /// <summary>Stops the load balancer when it is a service.</summary>
    protected override Task DoStopAsync(CancellationToken cancellationToken) => StopServiceAsync(LoadBalancer, cancellationToken);
}
