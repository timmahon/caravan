namespace Caravan.Aggregation;

/// <summary>An aggregation strategy backed by a function; see <see cref="AggregationStrategies.From"/>.</summary>
public sealed class DelegateAggregationStrategy : IAggregationStrategy
{
    private readonly Func<Exchange?, Exchange, Exchange?> _aggregate;
    private readonly Action<Exchange>? _onCompletion;

    /// <summary>Creates the strategy.</summary>
    /// <param name="aggregate">Aggregates the new exchange into the old one (null on the first call) and returns the result.</param>
    /// <param name="propagateException">
    /// When true, failed exchanges are not passed to <paramref name="aggregate"/>: their exception is set on the result so far
    /// (or the failed exchange becomes the result when it is the first one), so the EIP fails.
    /// </param>
    /// <param name="onCompletion">Optional callback run on the final result.</param>
    public DelegateAggregationStrategy(Func<Exchange?, Exchange, Exchange?> aggregate, bool propagateException = false, Action<Exchange>? onCompletion = null)
    {
        _aggregate = aggregate;
        _onCompletion = onCompletion;
        PropagateException = propagateException;
    }

    /// <summary>Whether failed exchanges skip the function and propagate their exception to the result.</summary>
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

        return _aggregate(oldExchange, newExchange);
    }

    /// <inheritdoc />
    public void OnCompletion(Exchange exchange, Exchange? inputExchange) => _onCompletion?.Invoke(exchange);

    /// <inheritdoc />
    public override string ToString() => "DelegateAggregationStrategy";
}
