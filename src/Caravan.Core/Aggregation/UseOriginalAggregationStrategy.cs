namespace Caravan.Aggregation;

/// <summary>
/// Keeps the original exchange: the one that entered the EIP (Camel's <c>UseOriginalAggregationStrategy</c>, the default of
/// <c>split</c>). With <see cref="PropagateException"/> (the default) the exception of a failed exchange is set on the original.
/// </summary>
/// <remarks>
/// Used by an EIP, the strategy is bound to each input exchange with <see cref="NewInstance"/>. Only the exception and the
/// redelivery-exhausted flag of the original are changed, never its message or properties, so it is safe while branches still copy it.
/// </remarks>
public sealed class UseOriginalAggregationStrategy : IAggregationStrategy
{
    /// <summary>Creates the strategy.</summary>
    /// <param name="original">The exchange to return; null until bound with <see cref="NewInstance"/> (the old exchange is returned meanwhile).</param>
    /// <param name="propagateException">Whether the exception of a failed exchange is set on the original.</param>
    public UseOriginalAggregationStrategy(Exchange? original = null, bool propagateException = true)
    {
        Original = original;
        PropagateException = propagateException;
    }

    /// <summary>The exchange returned as the result, if bound.</summary>
    public Exchange? Original { get; }

    /// <summary>Whether the exception of a failed exchange is set on the original.</summary>
    public bool PropagateException { get; }

    /// <summary>Creates a strategy with the same options bound to <paramref name="original"/>.</summary>
    /// <param name="original">The exchange to return as the result.</param>
    /// <returns>The bound strategy.</returns>
    public UseOriginalAggregationStrategy NewInstance(Exchange original) => new(original, PropagateException);

    /// <inheritdoc />
    public Exchange? Aggregate(Exchange? oldExchange, Exchange newExchange)
    {
        var answer = Original ?? oldExchange ?? newExchange;
        if (PropagateException && answer.Exception is null)
        {
            if (oldExchange is not null && oldExchange.Exception is not null)
            {
                AggregationHelper.PropagateException(answer, oldExchange);
            }
            else
            {
                AggregationHelper.PropagateException(answer, newExchange);
            }
        }

        return answer;
    }

    /// <inheritdoc />
    public override string ToString() => "UseOriginalAggregationStrategy";
}
