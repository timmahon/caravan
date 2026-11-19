namespace Caravan.Aggregation;

/// <summary>
/// Groups the bodies into a <see cref="List{T}"/> of <see cref="object"/> set as the body of the first exchange when the
/// aggregation completes (Camel's <c>GroupedBodyAggregationStrategy</c>). Null bodies are skipped. Failures are not propagated.
/// </summary>
public sealed class GroupedBodyAggregationStrategy : IAggregationStrategy
{
    /// <inheritdoc />
    public Exchange? Aggregate(Exchange? oldExchange, Exchange newExchange)
    {
        var result = oldExchange ?? newExchange;
        var list = AggregationHelper.GetList<object?>(result);
        if (newExchange.Message.Body is { } body)
        {
            list.Add(body);
        }

        return result;
    }

    /// <inheritdoc />
    public void OnCompletion(Exchange exchange, Exchange? inputExchange) => AggregationHelper.StoreListAsBody(exchange);

    /// <inheritdoc />
    public override string ToString() => "GroupedBodyAggregationStrategy";
}
