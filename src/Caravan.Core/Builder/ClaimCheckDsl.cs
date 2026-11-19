using Caravan.Aggregation;
using Caravan.Model;

namespace Caravan.Builder;

/// <summary>DSL for the <c>claimCheck</c> EIP.</summary>
public static class ClaimCheckDsl
{
    /// <summary>
    /// Stores data of the exchange for later or merges stored data back: <c>.ClaimCheck(ClaimCheckOperation.Set, "order")</c> ...
    /// <c>.ClaimCheck(ClaimCheckOperation.Get, "order", filter: "header:order*")</c>. See <see cref="ClaimCheckAggregationStrategy"/> for the
    /// filter syntax (<c>body</c>, <c>headers</c>, <c>header:pattern</c>, prefixed with <c>+</c> or <c>-</c>).
    /// </summary>
    /// <param name="steps">The block to add to.</param>
    /// <param name="operation">The operation.</param>
    /// <param name="key">The key (required for <c>Set</c>, <c>Get</c> and <c>GetAndRemove</c>); may contain <c>${...}</c> tokens.</param>
    /// <param name="filter">What <c>Get</c>, <c>GetAndRemove</c> and <c>Pop</c> merge back; null for the body and all headers.</param>
    /// <param name="aggregationStrategy">Merges retrieved data back instead of the filter-based default.</param>
    /// <param name="aggregationStrategyRef">The name of an <see cref="IAggregationStrategy"/> in the registry, used when <paramref name="aggregationStrategy"/> is null.</param>
    public static T ClaimCheck<T>(
        this T steps,
        ClaimCheckOperation operation,
        string? key = null,
        string? filter = null,
        IAggregationStrategy? aggregationStrategy = null,
        string? aggregationStrategyRef = null)
        where T : RouteSteps
    {
        steps.AddStep(new ClaimCheckDefinition(operation)
        {
            Key = key,
            Filter = filter,
            AggregationStrategy = aggregationStrategy,
            AggregationStrategyRef = aggregationStrategyRef,
        });
        return steps;
    }
}
