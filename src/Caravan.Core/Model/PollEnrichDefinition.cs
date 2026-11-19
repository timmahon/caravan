using Caravan.Aggregation;
using Caravan.Processors;
using Caravan.Spi;
using Caravan.Support;

namespace Caravan.Model;

/// <summary>
/// The <c>pollEnrich</c> EIP: receives a message from a resource endpoint with a polling consumer and merges it into the exchange (by
/// default the exchange takes the received message, or a null body when none was received in time); see <see cref="PollEnrichProcessor"/>.
/// </summary>
public sealed class PollEnrichDefinition(IExpression uri) : ProcessorDefinition
{
    /// <summary>Computes the resource URI (or endpoint).</summary>
    public IExpression Uri { get; set; } = uri;

    /// <summary>How long to wait for a message: null or negative (the default) blocks until one arrives, zero does not wait.</summary>
    public TimeSpan? Timeout { get; set; }

    /// <summary>Merges the received message into the exchange; null for <see cref="CopyAggregationStrategy"/>.</summary>
    public IAggregationStrategy? AggregationStrategy { get; set; }

    /// <summary>The name of an <see cref="IAggregationStrategy"/> in the registry, used when <see cref="AggregationStrategy"/> is null.</summary>
    public string? AggregationStrategyRef { get; set; }

    /// <summary>Whether a failed received exchange is given to the strategy instead of failing the exchange.</summary>
    public bool AggregateOnException { get; set; }

    /// <summary>Whether an unresolvable URI is skipped instead of failing the exchange.</summary>
    public bool IgnoreInvalidEndpoint { get; set; }

    /// <summary>Maximum cached polling consumers (default 1000; negative disables caching).</summary>
    public int CacheSize { get; set; } = ConsumerCache.DefaultCacheSize;

    /// <summary>
    /// The variable receiving the polled message (Camel's <c>variableReceive</c>: body as the variable, headers as <c>name.header.key</c>); the
    /// message is then left unchanged.
    /// </summary>
    public string? VariableReceive { get; set; }

    /// <inheritdoc />
    public override string ShortName => "pollEnrich";

    /// <inheritdoc />
    public override string Label => $"pollEnrich[{Uri}]";

    /// <inheritdoc />
    public override IProcessor CreateProcessor(RouteContext routeContext) =>
        new PollEnrichProcessor(
            Uri,
            Timeout,
            AggregationStrategy ?? RegistryLookup.Lookup<IAggregationStrategy>(routeContext.Context, AggregationStrategyRef),
            AggregateOnException,
            IgnoreInvalidEndpoint,
            CacheSize)
        {
            VariableReceive = VariableReceive,
        };
}
