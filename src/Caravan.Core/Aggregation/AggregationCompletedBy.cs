namespace Caravan.Aggregation;

/// <summary>The values of <see cref="ExchangePropertyKeys.AggregatedCompletedBy"/>: what completed an <c>aggregate</c> group.</summary>
public static class AggregationCompletedBy
{
    /// <summary>The group reached its completion size.</summary>
    public const string Size = "size";

    /// <summary>The group received no exchange for its completion timeout.</summary>
    public const string Timeout = "timeout";

    /// <summary>The completion interval elapsed.</summary>
    public const string Interval = "interval";

    /// <summary>The completion predicate matched.</summary>
    public const string Predicate = "predicate";

    /// <summary>The batch consumer's batch was complete (<c>completionFromBatchConsumer</c>).</summary>
    public const string Consumer = "consumer";

    /// <summary>
    /// Completion was forced: by an <see cref="Spi.IAggregateController"/>, the <see cref="ExchangePropertyKeys.AggregationCompleteAllGroups"/>
    /// header or property, a new correlation group (<c>completionOnNewCorrelationGroup</c>) or stopping (<c>forceCompletionOnStop</c>).
    /// </summary>
    public const string Force = "force";

    /// <summary>
    /// The aggregation strategy completed the group: <see cref="IAggregationStrategy.PreComplete"/>, the
    /// <see cref="ExchangePropertyKeys.AggregationCompleteCurrentGroup"/> property, or a failure with <c>discardOnAggregationFailure</c>.
    /// </summary>
    public const string Strategy = "strategy";
}
