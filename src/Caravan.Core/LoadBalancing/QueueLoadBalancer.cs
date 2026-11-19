using Caravan.Spi;

namespace Caravan.LoadBalancing;

/// <summary>Base class of load balancers that send each exchange to a single processor they choose (Camel's <c>QueueLoadBalancer</c>).</summary>
public abstract class QueueLoadBalancer : ILoadBalancer
{
    /// <inheritdoc />
    /// <exception cref="CaravanExchangeException">There are no processors.</exception>
    public ValueTask ProcessAsync(Exchange exchange, IReadOnlyList<IProcessor> processors)
    {
        if (processors.Count == 0)
        {
            throw new CaravanExchangeException("No processors available to process", exchange);
        }

        return processors[ChooseIndex(exchange, processors.Count)].ProcessAsync(exchange);
    }

    /// <summary>Chooses the processor for <paramref name="exchange"/>.</summary>
    /// <param name="exchange">The exchange.</param>
    /// <param name="count">The number of processors (positive).</param>
    /// <returns>The index of the processor, from 0 to <paramref name="count"/> - 1.</returns>
    protected abstract int ChooseIndex(Exchange exchange, int count);
}
