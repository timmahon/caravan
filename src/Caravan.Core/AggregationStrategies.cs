using Caravan.Aggregation;

namespace Caravan;

/// <summary>Factory methods for the built-in aggregation strategies (Camel's <c>AggregationStrategies</c>).</summary>
public static class AggregationStrategies
{
    /// <summary>Keeps the latest exchange, propagating failures; see <see cref="UseLatestAggregationStrategy"/>.</summary>
    public static IAggregationStrategy UseLatest() => UseLatestAggregationStrategy.Instance;

    /// <summary>Keeps the exchange that entered the EIP; see <see cref="UseOriginalAggregationStrategy"/>.</summary>
    /// <param name="propagateException">Whether the exception of a failed exchange is set on the original (default true).</param>
    public static IAggregationStrategy UseOriginal(bool propagateException = true) => new UseOriginalAggregationStrategy(null, propagateException);

    /// <summary>Groups the exchanges into a <c>List&lt;Exchange&gt;</c> body; see <see cref="GroupedExchangeAggregationStrategy"/>.</summary>
    public static IAggregationStrategy GroupedExchange() => new GroupedExchangeAggregationStrategy();

    /// <summary>Groups the bodies into a <c>List&lt;object?&gt;</c> body; see <see cref="GroupedBodyAggregationStrategy"/>.</summary>
    public static IAggregationStrategy GroupedBody() => new GroupedBodyAggregationStrategy();

    /// <summary>Groups the messages into a <c>List&lt;Message&gt;</c> body; see <see cref="GroupedMessageAggregationStrategy"/>.</summary>
    public static IAggregationStrategy GroupedMessage() => new GroupedMessageAggregationStrategy();

    /// <summary>Concatenates the bodies (or <paramref name="pick"/>) into a string body; see <see cref="StringAggregationStrategy"/>.</summary>
    /// <param name="delimiter">Inserted between values; null for none.</param>
    /// <param name="pick">The value to take from each exchange; null for the body.</param>
    public static IAggregationStrategy String(string? delimiter = null, IExpression? pick = null) => new StringAggregationStrategy(delimiter, pick);

    /// <summary>
    /// A strategy backed by a function of the result so far (null on the first call) and the new exchange:
    /// <c>AggregationStrategies.From((old, @new) =&gt; ...)</c>. Failed exchanges are passed to the function, which handles the
    /// failure by returning an exchange without the exception, unless <paramref name="propagateException"/> is true.
    /// </summary>
    /// <param name="aggregate">Returns the new result.</param>
    /// <param name="propagateException">Skip failed exchanges and set their exception on the result (the EIP then fails).</param>
    /// <param name="onCompletion">Optional callback run on the final result.</param>
    public static IAggregationStrategy From(Func<Exchange?, Exchange, Exchange?> aggregate, bool propagateException = false, Action<Exchange>? onCompletion = null) =>
        new DelegateAggregationStrategy(aggregate, propagateException, onCompletion);

    /// <summary>
    /// Aggregates typed bodies: <c>AggregationStrategies.Body((int sum, int n) =&gt; sum + n)</c> or
    /// <c>AggregationStrategies.Body((string? all, string s) =&gt; all + s)</c>. The function gets the result so far (default on
    /// the first call) and the next body; failed exchanges are skipped and fail the result unless <paramref name="propagateException"/> is false.
    /// </summary>
    /// <typeparam name="TItem">The type the body of each exchange is converted to.</typeparam>
    /// <typeparam name="TResult">The type of the aggregated body.</typeparam>
    /// <param name="aggregate">Combines the result so far with the next body.</param>
    /// <param name="propagateException">Whether failed exchanges are skipped and fail the result (default true).</param>
    public static IAggregationStrategy Body<TItem, TResult>(Func<TResult?, TItem, TResult> aggregate, bool propagateException = true) =>
        new BodyAggregationStrategy<TItem, TResult>(aggregate, propagateException);

    /// <summary>A configurable strategy: <c>AggregationStrategies.Flexible&lt;string&gt;().Pick(Expr.Header("id")).AccumulateInCollection()</c>.</summary>
    /// <typeparam name="T">The type values are converted to.</typeparam>
    public static FlexibleAggregationStrategy<T> Flexible<T>() => new();
}
