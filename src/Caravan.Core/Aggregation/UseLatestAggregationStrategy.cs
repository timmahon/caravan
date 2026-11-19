namespace Caravan.Aggregation;

/// <summary>
/// Keeps the latest exchange (Camel's <c>UseLatestAggregationStrategy</c>, the default of <c>multicast</c> and
/// <c>recipientList</c>). Failures are propagated: once an exchange failed, the result keeps its exception; an exchange that
/// was rolled back or had its failure handled by an error handler is kept as the result.
/// </summary>
public sealed class UseLatestAggregationStrategy : IAggregationStrategy
{
    /// <summary>A shared instance (the strategy is stateless).</summary>
    public static UseLatestAggregationStrategy Instance { get; } = new();

    /// <inheritdoc />
    public Exchange? Aggregate(Exchange? oldExchange, Exchange newExchange)
    {
        if (oldExchange is null)
        {
            return newExchange;
        }

        // Propagate an earlier exception to the new exchange, unless it failed itself.
        if (newExchange.Exception is null && oldExchange.Exception is not null)
        {
            AggregationHelper.PropagateException(newExchange, oldExchange);
            if (oldExchange.GetProperty(ExchangePropertyKeys.FailureEndpoint) is { } failureEndpoint)
            {
                newExchange.SetProperty(ExchangePropertyKeys.FailureEndpoint, failureEndpoint);
            }
        }

        if (newExchange.Exception is not null)
        {
            return newExchange;
        }

        return oldExchange.IsRollbackOnly || oldExchange.IsRollbackOnlyLast || oldExchange.ErrorHandlerHandled ? oldExchange : newExchange;
    }

    /// <inheritdoc />
    public override string ToString() => "UseLatestAggregationStrategy";
}
