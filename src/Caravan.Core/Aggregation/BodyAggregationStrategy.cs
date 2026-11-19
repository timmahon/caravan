namespace Caravan.Aggregation;

/// <summary>
/// Aggregates typed bodies with a function: the result so far (the old body as <typeparamref name="TResult"/>, default on the
/// first call) and the new body (as <typeparamref name="TItem"/>) give the new result body, kept on the first exchange. See
/// <see cref="AggregationStrategies.Body{TItem, TResult}"/>.
/// </summary>
/// <typeparam name="TItem">The type the body of each exchange is converted to.</typeparam>
/// <typeparam name="TResult">The type of the aggregated body.</typeparam>
public sealed class BodyAggregationStrategy<TItem, TResult> : IAggregationStrategy
{
    private readonly Func<TResult?, TItem, TResult> _aggregate;

    /// <summary>Creates the strategy.</summary>
    /// <param name="aggregate">Combines the result so far (default on the first call) with the next body.</param>
    /// <param name="propagateException">When true, failed exchanges are skipped and their exception is set on the result.</param>
    public BodyAggregationStrategy(Func<TResult?, TItem, TResult> aggregate, bool propagateException = true)
    {
        _aggregate = aggregate;
        PropagateException = propagateException;
    }

    /// <summary>Whether failed exchanges are skipped and their exception set on the result (default true).</summary>
    public bool PropagateException { get; }

    /// <inheritdoc />
    public Exchange? Aggregate(Exchange? oldExchange, Exchange newExchange)
    {
        if (PropagateException && newExchange.Exception is not null)
        {
            if (oldExchange is null)
            {
                return newExchange;
            }

            if (oldExchange.Exception is null)
            {
                AggregationHelper.PropagateException(oldExchange, newExchange);
            }

            return oldExchange;
        }

        if (PropagateException && oldExchange?.Exception is not null)
        {
            // The result already failed: nothing more to aggregate.
            return oldExchange;
        }

        var item = newExchange.Message.GetBody<TItem>()!;
        if (oldExchange is null)
        {
            newExchange.Message.Body = _aggregate(default, item);
            return newExchange;
        }

        oldExchange.Message.Body = _aggregate(oldExchange.Message.GetBody<TResult>(), item);
        return oldExchange;
    }

    /// <inheritdoc />
    public override string ToString() => $"BodyAggregationStrategy<{typeof(TItem).Name},{typeof(TResult).Name}>";
}
