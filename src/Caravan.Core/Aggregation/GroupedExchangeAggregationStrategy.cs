namespace Caravan.Aggregation;

/// <summary>
/// Groups the exchanges into a <see cref="List{T}"/> of <see cref="Exchange"/> set as the body of a new holder exchange when the
/// aggregation completes (Camel's <c>GroupedExchangeAggregationStrategy</c>). Failures are not propagated.
/// </summary>
public sealed class GroupedExchangeAggregationStrategy : IAggregationStrategy
{
    /// <inheritdoc />
    public Exchange? Aggregate(Exchange? oldExchange, Exchange newExchange)
    {
        // The holder must not be one of the grouped exchanges (it would contain itself).
        oldExchange ??= AggregationHelper.CreateHolder(newExchange);
        AggregationHelper.GetList<Exchange>(oldExchange).Add(newExchange);
        return oldExchange;
    }

    /// <inheritdoc />
    public void OnCompletion(Exchange exchange, Exchange? inputExchange) => AggregationHelper.StoreListAsBody(exchange);

    /// <inheritdoc />
    public override string ToString() => "GroupedExchangeAggregationStrategy";
}
