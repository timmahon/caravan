using Caravan.Language;
using Caravan.Model;
using Caravan.Processors;
using Caravan.Support;

namespace Caravan.Builder;

/// <summary>DSL for the <c>toD</c> EIP (dynamic to).</summary>
public static class ToDynamicDsl
{
    /// <summary>
    /// Sends to a URI computed per exchange from <paramref name="uri"/>, which may contain <c>${header.x}</c>,
    /// <c>${body}</c>, <c>${exchangeProperty.x}</c> functions of the Simple language (see <see cref="SimpleLanguage"/>), e.g. <c>.ToD("mock:${header.target}")</c>.
    /// </summary>
    /// <param name="steps">The block to add to.</param>
    /// <param name="uri">The URI template.</param>
    /// <param name="pattern">The exchange pattern to send with; null keeps the exchange's pattern.</param>
    /// <param name="cacheSize">Maximum cached producers (default 1000; negative disables caching).</param>
    /// <param name="ignoreInvalidEndpoint">Skip, instead of failing, when the URI cannot be resolved.</param>
    /// <param name="variableSend">The variable sent as the body instead of the message body (Camel's <c>variableSend</c>).</param>
    /// <param name="variableReceive">The variable receiving the reply; the message is then left unchanged (Camel's <c>variableReceive</c>).</param>
    public static T ToD<T>(this T steps, string uri, ExchangePattern? pattern = null, int cacheSize = SendDynamicProcessor.DefaultCacheSize, bool ignoreInvalidEndpoint = false, string? variableSend = null, string? variableReceive = null)
        where T : RouteSteps => steps.ToD(SimpleLanguage.Expression(uri), pattern, cacheSize, ignoreInvalidEndpoint, variableSend, variableReceive);

    /// <summary>Sends to the URI (or endpoint) returned by <paramref name="uri"/>.</summary>
    /// <param name="steps">The block to add to.</param>
    /// <param name="uri">Computes the URI or endpoint.</param>
    /// <param name="pattern">The exchange pattern to send with; null keeps the exchange's pattern.</param>
    /// <param name="cacheSize">Maximum cached producers (default 1000; negative disables caching).</param>
    /// <param name="ignoreInvalidEndpoint">Skip, instead of failing, when the URI cannot be resolved.</param>
    /// <param name="variableSend">The variable sent as the body instead of the message body (Camel's <c>variableSend</c>).</param>
    /// <param name="variableReceive">The variable receiving the reply; the message is then left unchanged (Camel's <c>variableReceive</c>).</param>
    public static T ToD<T>(this T steps, IExpression uri, ExchangePattern? pattern = null, int cacheSize = SendDynamicProcessor.DefaultCacheSize, bool ignoreInvalidEndpoint = false, string? variableSend = null, string? variableReceive = null)
        where T : RouteSteps
    {
        steps.AddStep(new ToDynamicDefinition(uri) { Pattern = pattern, CacheSize = cacheSize, IgnoreInvalidEndpoint = ignoreInvalidEndpoint, VariableSend = variableSend, VariableReceive = variableReceive });
        return steps;
    }

    /// <summary>Sends to the URI returned by <paramref name="uri"/>.</summary>
    /// <param name="steps">The block to add to.</param>
    /// <param name="uri">Computes the URI.</param>
    /// <param name="pattern">The exchange pattern to send with; null keeps the exchange's pattern.</param>
    /// <param name="cacheSize">Maximum cached producers (default 1000; negative disables caching).</param>
    /// <param name="ignoreInvalidEndpoint">Skip, instead of failing, when the URI cannot be resolved.</param>
    /// <param name="variableSend">The variable sent as the body instead of the message body (Camel's <c>variableSend</c>).</param>
    /// <param name="variableReceive">The variable receiving the reply; the message is then left unchanged (Camel's <c>variableReceive</c>).</param>
    public static T ToD<T>(this T steps, Func<Exchange, string> uri, ExchangePattern? pattern = null, int cacheSize = SendDynamicProcessor.DefaultCacheSize, bool ignoreInvalidEndpoint = false, string? variableSend = null, string? variableReceive = null)
        where T : RouteSteps => steps.ToD(new FuncExpression(uri), pattern, cacheSize, ignoreInvalidEndpoint, variableSend, variableReceive);
}
