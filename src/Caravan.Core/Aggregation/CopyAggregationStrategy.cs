using Caravan.Support;

namespace Caravan.Aggregation;

/// <summary>
/// The default strategy of <c>enrich</c> and <c>pollEnrich</c> (Camel's enricher <c>CopyAggregationStrategy</c>): the exchange takes the
/// resource's reply (message, merged properties, failure). When <c>pollEnrich</c> received nothing, the body is set to null and the headers are
/// kept, to show that no data was received.
/// </summary>
public sealed class CopyAggregationStrategy : IAggregationStrategy
{
    /// <summary>A shared instance (the strategy is stateless).</summary>
    public static CopyAggregationStrategy Instance { get; } = new();

    /// <inheritdoc />
    public Exchange? Aggregate(Exchange? oldExchange, Exchange newExchange)
    {
        if (oldExchange is null)
        {
            return newExchange;
        }

        ExchangeHelper.CopyResults(oldExchange, newExchange);
        return oldExchange;
    }

    /// <inheritdoc />
    public Exchange? AggregateNoMessage(Exchange oldExchange)
    {
        oldExchange.Message.Body = null;
        return oldExchange;
    }

    /// <inheritdoc />
    public override string ToString() => "CopyAggregationStrategy";
}
