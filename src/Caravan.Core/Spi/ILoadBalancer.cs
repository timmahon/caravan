namespace Caravan.Spi;

/// <summary>
/// Chooses which of the <c>loadBalance</c> EIP's processors handle an exchange (Camel's <c>LoadBalancer</c>). Built-ins are in
/// <c>Caravan.LoadBalancing</c>; derive from <see cref="LoadBalancing.QueueLoadBalancer"/> to pick a single processor per exchange.
/// </summary>
/// <remarks>
/// One instance serves one <c>loadBalance</c> node and is called concurrently for different exchanges: keep state thread-safe. The processors are
/// the node's outputs, each wrapped like any route node (exceptions are recorded on <see cref="Exchange.Exception"/> rather than thrown, and
/// the route's error handler applies unless the failover load balancer disables it). A load balancer that is an <see cref="IService"/> is started
/// and stopped with the route.
/// </remarks>
public interface ILoadBalancer
{
    /// <summary>Processes <paramref name="exchange"/> with one or more of <paramref name="processors"/>.</summary>
    /// <param name="exchange">The exchange.</param>
    /// <param name="processors">The processors to balance over, in definition order.</param>
    ValueTask ProcessAsync(Exchange exchange, IReadOnlyList<IProcessor> processors);
}
