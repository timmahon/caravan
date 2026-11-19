using Caravan.Language;
using Caravan.Model;
using Caravan.Processors;

namespace Caravan.Builder;

/// <summary>DSL for the <c>routingSlip</c> and <c>dynamicRouter</c> EIPs.</summary>
public static class RoutingSlipDsl
{
    /// <summary>
    /// Sends the exchange through the endpoints computed once by <paramref name="expression"/> (URIs separated by
    /// <paramref name="uriDelimiter"/>, a sequence of URIs or endpoints, or an endpoint), in sequence, each step's output being the next
    /// step's input: <c>.RoutingSlip(Expr.Header("slip"))</c>. <see cref="ExchangePropertyKeys.SlipEndpoint"/> holds the current endpoint.
    /// </summary>
    /// <param name="steps">The block to add to.</param>
    /// <param name="expression">Computes the slip.</param>
    /// <param name="uriDelimiter">Splits a string slip (default <c>,</c>); null to not split.</param>
    /// <param name="ignoreInvalidEndpoints">Skip endpoints that cannot be resolved instead of failing the exchange.</param>
    /// <param name="cacheSize">Maximum cached producers (default 1000; negative disables caching).</param>
    public static T RoutingSlip<T>(this T steps, IExpression expression, string? uriDelimiter = ",", bool ignoreInvalidEndpoints = false, int cacheSize = ProducerCache.DefaultCacheSize)
        where T : RouteSteps
    {
        steps.AddStep(new RoutingSlipDefinition(expression) { UriDelimiter = uriDelimiter, IgnoreInvalidEndpoints = ignoreInvalidEndpoints, CacheSize = cacheSize });
        return steps;
    }

    /// <summary>
    /// Sends the exchange through the endpoints of <paramref name="slip"/>, a Simple expression (see <see cref="SimpleLanguage"/>) evaluated once,
    /// e.g. <c>.RoutingSlip("${header.slip}")</c> or <c>.RoutingSlip("direct:a,mock:${header.target}")</c>.
    /// </summary>
    /// <param name="steps">The block to add to.</param>
    /// <param name="slip">The slip as a Simple expression.</param>
    /// <param name="uriDelimiter">Splits a string slip (default <c>,</c>); null to not split.</param>
    /// <param name="ignoreInvalidEndpoints">Skip endpoints that cannot be resolved instead of failing the exchange.</param>
    /// <param name="cacheSize">Maximum cached producers (default 1000; negative disables caching).</param>
    /// <exception cref="Language.Simple.SimpleParseException">The slip is not a valid Simple expression.</exception>
    public static T RoutingSlip<T>(this T steps, string slip, string? uriDelimiter = ",", bool ignoreInvalidEndpoints = false, int cacheSize = ProducerCache.DefaultCacheSize)
        where T : RouteSteps => steps.RoutingSlip(SimpleLanguage.Expression(slip), uriDelimiter, ignoreInvalidEndpoints, cacheSize);

    /// <summary>Sends the exchange through the endpoints returned once by <paramref name="expression"/>, in sequence.</summary>
    /// <param name="steps">The block to add to.</param>
    /// <param name="expression">Computes the slip.</param>
    /// <param name="uriDelimiter">Splits a string slip (default <c>,</c>); null to not split.</param>
    /// <param name="ignoreInvalidEndpoints">Skip endpoints that cannot be resolved instead of failing the exchange.</param>
    /// <param name="cacheSize">Maximum cached producers (default 1000; negative disables caching).</param>
    public static T RoutingSlip<T>(this T steps, Func<Exchange, object?> expression, string? uriDelimiter = ",", bool ignoreInvalidEndpoints = false, int cacheSize = ProducerCache.DefaultCacheSize)
        where T : RouteSteps => steps.RoutingSlip(new FuncExpression(expression), uriDelimiter, ignoreInvalidEndpoints, cacheSize);

    /// <summary>
    /// Sends the exchange to the endpoint(s) returned by <paramref name="expression"/>, evaluated again after each step, until it returns
    /// null: <c>.DynamicRouter(e =&gt; e.GetProperty(ExchangePropertyKeys.SlipEndpoint) is null ? "direct:a" : null)</c>. The expression sees
    /// the last endpoint in <see cref="ExchangePropertyKeys.SlipEndpoint"/> and the evaluation number in
    /// <see cref="ExchangePropertyKeys.DynamicRouterIndex"/>. As in Camel, an expression that never returns null routes forever.
    /// </summary>
    /// <param name="steps">The block to add to.</param>
    /// <param name="expression">Computes the next endpoint(s); null ends the routing.</param>
    /// <param name="uriDelimiter">Splits a string of endpoints (default <c>,</c>); null to not split.</param>
    /// <param name="ignoreInvalidEndpoints">Skip endpoints that cannot be resolved instead of failing the exchange.</param>
    /// <param name="cacheSize">Maximum cached producers (default 1000; negative disables caching).</param>
    public static T DynamicRouter<T>(this T steps, IExpression expression, string? uriDelimiter = ",", bool ignoreInvalidEndpoints = false, int cacheSize = ProducerCache.DefaultCacheSize)
        where T : RouteSteps
    {
        steps.AddStep(new DynamicRouterDefinition(expression) { UriDelimiter = uriDelimiter, IgnoreInvalidEndpoints = ignoreInvalidEndpoints, CacheSize = cacheSize });
        return steps;
    }

    /// <summary>Sends the exchange to the endpoint(s) returned by <paramref name="expression"/>, evaluated again after each step, until it returns null.</summary>
    /// <param name="steps">The block to add to.</param>
    /// <param name="expression">Computes the next endpoint(s); null ends the routing.</param>
    /// <param name="uriDelimiter">Splits a string of endpoints (default <c>,</c>); null to not split.</param>
    /// <param name="ignoreInvalidEndpoints">Skip endpoints that cannot be resolved instead of failing the exchange.</param>
    /// <param name="cacheSize">Maximum cached producers (default 1000; negative disables caching).</param>
    public static T DynamicRouter<T>(this T steps, Func<Exchange, object?> expression, string? uriDelimiter = ",", bool ignoreInvalidEndpoints = false, int cacheSize = ProducerCache.DefaultCacheSize)
        where T : RouteSteps => steps.DynamicRouter(new FuncExpression(expression), uriDelimiter, ignoreInvalidEndpoints, cacheSize);
}
