using Caravan.Aggregation;
using Caravan.Processors;
using Caravan.Spi;

namespace Caravan.Model;

/// <summary>
/// The <c>recipientList</c> EIP: sends a copy of the exchange to each recipient computed by <see cref="Expression"/>; the replies are
/// aggregated (by default with <see cref="UseLatestAggregationStrategy"/>, as in Camel). See <see cref="RecipientListProcessor"/>.
/// </summary>
public sealed class RecipientListDefinition(IExpression expression) : FanOutDefinition
{
    /// <summary>Computes the recipients (URIs or endpoints).</summary>
    public IExpression Expression { get; set; } = expression;

    /// <summary>Splits a string of recipients (default <c>,</c>); null or <c>false</c> to not split.</summary>
    public string? Delimiter { get; set; } = ",";

    /// <summary>Whether recipients that cannot be resolved are skipped instead of failing the exchange.</summary>
    public bool IgnoreInvalidEndpoints { get; set; }

    /// <summary>Maximum cached producers (default 1000; negative disables caching).</summary>
    public int CacheSize { get; set; } = ProducerCache.DefaultCacheSize;

    /// <inheritdoc />
    public override string ShortName => "recipientList";

    /// <inheritdoc />
    public override string Label => $"recipientList[{Expression}]";

    /// <inheritdoc />
    public override IProcessor CreateProcessor(RouteContext routeContext)
    {
        var settings = CreateSettings(routeContext, UseLatestAggregationStrategy.Instance);
        var errorHandler = routeContext.ErrorHandlerFactory;
        return new RecipientListProcessor(
            Expression,
            settings,
            Delimiter,
            IgnoreInvalidEndpoints,
            CacheSize,
            errorHandler is null ? null : sender => errorHandler.Wrap(routeContext, this, sender));
    }
}
