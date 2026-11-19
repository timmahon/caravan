namespace Caravan;

/// <summary>
/// Combines exchanges into one (Camel's <c>AggregationStrategy</c>): the replies of <c>multicast</c>, <c>split</c> and
/// <c>recipientList</c> branches, and (later) the exchanges of the <c>aggregate</c> EIP.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Aggregate(Exchange?, Exchange)"/> is called once per exchange: the first time with a null
/// old exchange, then with the exchange returned by the previous call. The returned exchange is the
/// result so far; it may be the old exchange, the new exchange or a new exchange.
/// </para>
/// <para>
/// Failed exchanges are aggregated too (unless <c>stopOnException</c> stops processing first): a strategy that returns an
/// exchange without the exception handles the failure. The built-in <see cref="Aggregation.UseLatestAggregationStrategy"/> and
/// <see cref="Aggregation.UseOriginalAggregationStrategy"/> propagate it.
/// </para>
/// <para>
/// By default the routing engine never calls a strategy concurrently (<c>parallelAggregate</c> lifts that for multicast-like EIPs),
/// but the same instance is shared by all exchanges going through the EIP, so per-aggregation state must be kept on the
/// exchanges (e.g. in a property, as the grouped strategies do), not in fields.
/// </para>
/// </remarks>
public interface IAggregationStrategy
{
    /// <summary>Aggregates <paramref name="newExchange"/> into the result so far.</summary>
    /// <param name="oldExchange">The result so far; null on the first call.</param>
    /// <param name="newExchange">The exchange to add.</param>
    /// <returns>The new result.</returns>
    Exchange? Aggregate(Exchange? oldExchange, Exchange newExchange);

    /// <summary>
    /// Aggregates <paramref name="newExchange"/> into the result so far, with access to the exchange that entered the EIP (the
    /// exchange being split or multicast). The default calls <see cref="Aggregate(Exchange?, Exchange)"/>.
    /// </summary>
    /// <param name="oldExchange">The result so far; null on the first call.</param>
    /// <param name="newExchange">The exchange to add.</param>
    /// <param name="inputExchange">The exchange that entered the EIP. Strategies must not modify it while branches run.</param>
    /// <returns>The new result.</returns>
    Exchange? Aggregate(Exchange? oldExchange, Exchange newExchange, Exchange inputExchange) => Aggregate(oldExchange, newExchange);

    /// <summary>
    /// Called once with the final result when the aggregation completes (Camel's <c>onCompletion</c>), e.g. to turn
    /// accumulated state into the body. Not called when nothing was aggregated.
    /// </summary>
    /// <param name="exchange">The final result.</param>
    /// <param name="inputExchange">The exchange that entered the EIP, when there is one.</param>
    void OnCompletion(Exchange exchange, Exchange? inputExchange)
    {
    }

    /// <summary>
    /// Called when the aggregation times out, once for each exchange that did not complete in time (Camel's <c>timeout</c>):
    /// for <c>multicast</c>-like EIPs with a <c>timeout</c>, for every branch that had not completed.
    /// </summary>
    /// <param name="exchange">The result so far, or the input exchange when nothing was aggregated yet.</param>
    /// <param name="index">The index of the branch that did not complete.</param>
    /// <param name="total">The number of branches started.</param>
    /// <param name="timeout">The timeout that elapsed.</param>
    void OnTimeout(Exchange exchange, int index, int total, TimeSpan timeout)
    {
    }

    /// <summary>Whether the strategy can complete an aggregation early with <see cref="PreComplete"/> (used by the <c>aggregate</c> EIP).</summary>
    bool CanPreComplete => false;

    /// <summary>
    /// Whether the aggregation so far must complete before <paramref name="newExchange"/> is aggregated; the new exchange then
    /// starts a new aggregation (used by the <c>aggregate</c> EIP when <see cref="CanPreComplete"/> is true).
    /// </summary>
    /// <param name="oldExchange">The result so far; null on the first call.</param>
    /// <param name="newExchange">The exchange about to be aggregated.</param>
    /// <returns>True to complete the aggregation before <paramref name="newExchange"/>.</returns>
    bool PreComplete(Exchange? oldExchange, Exchange newExchange) => false;
}
