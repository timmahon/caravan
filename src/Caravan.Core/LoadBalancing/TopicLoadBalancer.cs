using Caravan.Spi;

namespace Caravan.LoadBalancing;

/// <summary>
/// Sends a correlated copy of each exchange to every processor, in order (Camel's <c>TopicLoadBalancer</c>). The exchange itself is not changed by
/// the processors; when one fails, its exception is set on the exchange and the remaining processors are skipped.
/// </summary>
public sealed class TopicLoadBalancer : ILoadBalancer
{
    /// <inheritdoc />
    public async ValueTask ProcessAsync(Exchange exchange, IReadOnlyList<IProcessor> processors)
    {
        foreach (var processor in processors)
        {
            var copy = exchange.CreateCorrelatedCopy();
            copy.UnitOfWork = exchange.UnitOfWork;
            try
            {
                await processor.ProcessAsync(copy).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                copy.Exception = e;
            }

            if (copy.Exception is { } exception)
            {
                exchange.Exception = exception;
                exchange.IsRedeliveryExhausted = copy.IsRedeliveryExhausted;
                return;
            }
        }
    }

    /// <inheritdoc />
    public override string ToString() => "TopicLoadBalancer";
}
