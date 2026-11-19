namespace Caravan.Aggregation;

/// <summary>
/// Groups the messages into a <see cref="List{T}"/> of <see cref="Message"/> set as the body of a new holder exchange when the
/// aggregation completes (Camel's <c>GroupedMessageAggregationStrategy</c>). Failures are not propagated.
/// </summary>
public sealed class GroupedMessageAggregationStrategy : IAggregationStrategy
{
    /// <inheritdoc />
    public Exchange? Aggregate(Exchange? oldExchange, Exchange newExchange)
    {
        oldExchange ??= AggregationHelper.CreateHolder(newExchange);
        AggregationHelper.GetList<Message>(oldExchange).Add(newExchange.Message);
        return oldExchange;
    }

    /// <inheritdoc />
    public void OnCompletion(Exchange exchange, Exchange? inputExchange) => AggregationHelper.StoreListAsBody(exchange);

    /// <inheritdoc />
    public override string ToString() => "GroupedMessageAggregationStrategy";
}
