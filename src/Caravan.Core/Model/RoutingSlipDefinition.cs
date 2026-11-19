using Caravan.Processors;
using Caravan.Spi;

namespace Caravan.Model;

/// <summary>The <c>routingSlip</c> EIP: sends the exchange through the endpoints computed once by <see cref="Expression"/>, in sequence; see <see cref="RoutingSlipProcessor"/>.</summary>
public sealed class RoutingSlipDefinition(IExpression expression) : ProcessorDefinition
{
    /// <summary>Computes the slip (URIs or endpoints).</summary>
    public IExpression Expression { get; set; } = expression;

    /// <summary>Splits a string of URIs (default <c>,</c>); null to not split.</summary>
    public string? UriDelimiter { get; set; } = ",";

    /// <summary>Whether endpoints that cannot be resolved are skipped instead of failing the exchange.</summary>
    public bool IgnoreInvalidEndpoints { get; set; }

    /// <summary>Maximum cached producers (default 1000; negative disables caching).</summary>
    public int CacheSize { get; set; } = ProducerCache.DefaultCacheSize;

    /// <inheritdoc />
    public override string ShortName => "routingSlip";

    /// <inheritdoc />
    public override string Label => $"routingSlip[{Expression}]";

    /// <inheritdoc />
    public override IProcessor CreateProcessor(RouteContext routeContext)
    {
        var errorHandler = routeContext.ErrorHandlerFactory;
        return new RoutingSlipProcessor(
            Expression,
            UriDelimiter,
            IgnoreInvalidEndpoints,
            CacheSize,
            errorHandler is null ? null : sender => errorHandler.Wrap(routeContext, this, sender));
    }
}
