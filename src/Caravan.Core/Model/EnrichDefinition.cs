using Caravan.Aggregation;
using Caravan.Processors;
using Caravan.Spi;
using Caravan.Support;

namespace Caravan.Model;

/// <summary>
/// The <c>enrich</c> EIP: sends a copy of the exchange to a resource endpoint (request/reply) and merges the reply into the exchange (by
/// default the exchange takes the reply, <see cref="CopyAggregationStrategy"/>); see <see cref="EnrichProcessor"/>.
/// </summary>
public sealed class EnrichDefinition(IExpression uri) : ProcessorDefinition
{
    /// <summary>Computes the resource URI (or endpoint).</summary>
    public IExpression Uri { get; set; } = uri;

    /// <summary>Merges the reply into the exchange; null for <see cref="CopyAggregationStrategy"/>.</summary>
    public IAggregationStrategy? AggregationStrategy { get; set; }

    /// <summary>The name of an <see cref="IAggregationStrategy"/> in the registry, used when <see cref="AggregationStrategy"/> is null.</summary>
    public string? AggregationStrategyRef { get; set; }

    /// <summary>Whether a failed reply is given to the strategy instead of failing the exchange.</summary>
    public bool AggregateOnException { get; set; }

    /// <summary>Whether the resource exchange shares the exchange's unit of work.</summary>
    public bool ShareUnitOfWork { get; set; }

    /// <summary>Whether an unresolvable URI is skipped instead of failing the exchange.</summary>
    public bool IgnoreInvalidEndpoint { get; set; }

    /// <summary>Maximum cached producers (default 1000; negative disables caching).</summary>
    public int CacheSize { get; set; } = ProducerCache.DefaultCacheSize;

    /// <summary>The variable whose value is sent as the body (Camel's <c>variableSend</c>); null sends the message body.</summary>
    public string? VariableSend { get; set; }

    /// <summary>
    /// The variable receiving the reply (Camel's <c>variableReceive</c>: body as the variable, headers as <c>name.header.key</c>); the message
    /// is then left unchanged.
    /// </summary>
    public string? VariableReceive { get; set; }

    /// <inheritdoc />
    public override string ShortName => "enrich";

    /// <inheritdoc />
    public override string Label => $"enrich[{Uri}]";

    /// <inheritdoc />
    public override IProcessor CreateProcessor(RouteContext routeContext) =>
        new EnrichProcessor(
            Uri,
            AggregationStrategy ?? RegistryLookup.Lookup<IAggregationStrategy>(routeContext.Context, AggregationStrategyRef),
            AggregateOnException,
            ShareUnitOfWork,
            IgnoreInvalidEndpoint,
            CacheSize)
        {
            VariableSend = VariableSend,
            VariableReceive = VariableReceive,
        };
}
