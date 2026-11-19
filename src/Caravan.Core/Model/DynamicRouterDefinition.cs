using Caravan.Processors;
using Caravan.Spi;

namespace Caravan.Model;

/// <summary>The <c>dynamicRouter</c> EIP: sends the exchange to the endpoints returned by <see cref="Expression"/>, evaluated again after each step until it returns null; see <see cref="DynamicRouterProcessor"/>.</summary>
public sealed class DynamicRouterDefinition(IExpression expression) : ProcessorDefinition
{
    /// <summary>Computes the next endpoint(s) (URIs or endpoints); null ends the routing.</summary>
    public IExpression Expression { get; set; } = expression;

    /// <summary>Splits a string of URIs (default <c>,</c>); null to not split.</summary>
    public string? UriDelimiter { get; set; } = ",";

    /// <summary>Whether endpoints that cannot be resolved are skipped instead of failing the exchange.</summary>
    public bool IgnoreInvalidEndpoints { get; set; }

    /// <summary>Maximum cached producers (default 1000; negative disables caching).</summary>
    public int CacheSize { get; set; } = ProducerCache.DefaultCacheSize;

    /// <inheritdoc />
    public override string ShortName => "dynamicRouter";

    /// <inheritdoc />
    public override string Label => $"dynamicRouter[{Expression}]";

    /// <inheritdoc />
    public override IProcessor CreateProcessor(RouteContext routeContext)
    {
        var errorHandler = routeContext.ErrorHandlerFactory;
        return new DynamicRouterProcessor(
            Expression,
            UriDelimiter,
            IgnoreInvalidEndpoints,
            CacheSize,
            errorHandler is null ? null : sender => errorHandler.Wrap(routeContext, this, sender));
    }
}
