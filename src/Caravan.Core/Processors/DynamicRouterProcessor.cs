using Caravan.Support;

namespace Caravan.Processors;

/// <summary>
/// The <c>dynamicRouter</c> EIP (Camel's <c>DynamicRouter</c>): a routing slip whose expression is evaluated again after each step, until it
/// returns null (or no endpoint). Each evaluation may return several endpoints (separated by <see cref="RoutingSlipProcessor.UriDelimiter"/>),
/// sent to in turn before the next evaluation. The expression sees the endpoint last sent to in
/// <see cref="ExchangePropertyKeys.SlipEndpoint"/> and the number of the evaluation in <see cref="ExchangePropertyKeys.DynamicRouterIndex"/>.
/// </summary>
/// <remarks>
/// As in Camel there is no limit on the number of steps: an expression that never returns null routes forever. The loop stops when the
/// exchange fails, is stopped, or its <see cref="Exchange.CancellationToken"/> is cancelled (the exchange then fails).
/// </remarks>
public sealed class DynamicRouterProcessor : RoutingSlipProcessor
{
    /// <summary>Creates the processor.</summary>
    /// <param name="expression">Computes the next endpoint(s); null ends the routing.</param>
    /// <param name="uriDelimiter">Splits a string of endpoints (default <c>,</c>); null to not split.</param>
    /// <param name="ignoreInvalidEndpoints">Skips endpoints that cannot be resolved instead of failing.</param>
    /// <param name="cacheSize">Maximum cached producers; 0 uses the default, negative disables caching.</param>
    /// <param name="wrapStep">Wraps the sender of each step (with error handling).</param>
    public DynamicRouterProcessor(
        IExpression expression,
        string? uriDelimiter = ",",
        bool ignoreInvalidEndpoints = false,
        int cacheSize = ProducerCache.DefaultCacheSize,
        Func<Spi.IProcessor, Spi.IProcessor>? wrapStep = null)
        : base(expression, uriDelimiter, ignoreInvalidEndpoints, cacheSize, wrapStep)
    {
    }

    /// <inheritdoc />
    public override string ToString() => $"DynamicRouter[{Expression}]";

    /// <inheritdoc />
    protected override IEnumerable<object?> GetSlip(Exchange exchange)
    {
        for (var index = 0; ; index++)
        {
            exchange.CancellationToken.ThrowIfCancellationRequested();
            exchange.SetProperty(ExchangePropertyKeys.DynamicRouterIndex, index);
            var value = Expression.Evaluate(exchange);
            if (value is null)
            {
                yield break;
            }

            var any = false;
            foreach (var endpoint in SplitIterator.Create(value, UriDelimiter))
            {
                any = true;
                yield return endpoint;
            }

            if (!any)
            {
                yield break;
            }
        }
    }
}
