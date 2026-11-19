using Caravan.Language;
using Caravan.Aggregation;
using Caravan.Processors;
using Caravan.Spi;
using Caravan.Support;

namespace Caravan.Model;

/// <summary>
/// The <c>claimCheck</c> EIP: stores data of the exchange for later, or merges stored data back; see <see cref="ClaimCheckProcessor"/> and, for the
/// filter syntax, <see cref="ClaimCheckAggregationStrategy"/>.
/// </summary>
public sealed class ClaimCheckDefinition(ClaimCheckOperation operation) : ProcessorDefinition
{
    /// <summary>The operation.</summary>
    public ClaimCheckOperation Operation { get; set; } = operation;

    /// <summary>The key (required for <c>Set</c>, <c>Get</c> and <c>GetAndRemove</c>); may contain <c>${...}</c> functions of the Simple language (see <see cref="SimpleLanguage"/>).</summary>
    public string? Key { get; set; }

    /// <summary>What <c>Get</c>, <c>GetAndRemove</c> and <c>Pop</c> merge back, e.g. <c>body,header:foo</c>; null for everything.</summary>
    public string? Filter { get; set; }

    /// <summary>Merges retrieved data back instead of the filter-based default.</summary>
    public IAggregationStrategy? AggregationStrategy { get; set; }

    /// <summary>The name of an <see cref="IAggregationStrategy"/> in the registry, used when <see cref="AggregationStrategy"/> is null.</summary>
    public string? AggregationStrategyRef { get; set; }

    /// <inheritdoc />
    public override string ShortName => "claimCheck";

    /// <inheritdoc />
    public override string Label => Key is null ? $"claimCheck[{Operation}]" : $"claimCheck[{Operation} {Key}]";

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">The key is missing for an operation that needs it, or the filter is invalid.</exception>
    public override IProcessor CreateProcessor(RouteContext routeContext)
    {
        try
        {
            return new ClaimCheckProcessor(
                Operation,
                Key is null ? null : SimpleLanguage.Expression(Key),
                Filter,
                AggregationStrategy ?? RegistryLookup.Lookup<IAggregationStrategy>(routeContext.Context, AggregationStrategyRef));
        }
        catch (ArgumentException e)
        {
            throw new InvalidOperationException(e.Message, e);
        }
    }
}
