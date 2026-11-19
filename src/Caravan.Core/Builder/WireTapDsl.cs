using Caravan.Language;
using Caravan.Model;
using Caravan.Processors;
using Caravan.Support;

namespace Caravan.Builder;

/// <summary>DSL for the <c>wireTap</c> EIP.</summary>
public static class WireTapDsl
{
    /// <summary>
    /// Sends a copy of the exchange to <paramref name="uri"/> asynchronously (<c>InOnly</c>); the original continues
    /// immediately. The URI may contain <c>${...}</c> functions of the Simple language (see <see cref="SimpleLanguage"/>), evaluated on the tapped exchange.
    /// </summary>
    /// <param name="steps">The block to add to.</param>
    /// <param name="uri">The URI template.</param>
    /// <param name="copy">True (default) to tap a copy of the exchange; false to tap a new, empty exchange (as in Camel).</param>
    /// <param name="onPrepare">Optional callback to modify the tapped exchange before it is sent.</param>
    /// <param name="cacheSize">Maximum cached producers (default 1000; negative disables caching).</param>
    /// <param name="ignoreInvalidEndpoint">Skip the tap when the URI cannot be resolved.</param>
    public static T WireTap<T>(this T steps, string uri, bool copy = true, Action<Exchange>? onPrepare = null, int cacheSize = SendDynamicProcessor.DefaultCacheSize, bool ignoreInvalidEndpoint = false)
        where T : RouteSteps => steps.WireTap(SimpleLanguage.Expression(uri), copy, onPrepare, cacheSize, ignoreInvalidEndpoint);

    /// <summary>Wire taps to the URI computed by <paramref name="uri"/> on the tapped exchange.</summary>
    /// <param name="steps">The block to add to.</param>
    /// <param name="uri">Computes the URI or endpoint.</param>
    /// <param name="copy">True (default) to tap a copy of the exchange; false to tap a new, empty exchange.</param>
    /// <param name="onPrepare">Optional callback to modify the tapped exchange before it is sent.</param>
    /// <param name="cacheSize">Maximum cached producers (default 1000; negative disables caching).</param>
    /// <param name="ignoreInvalidEndpoint">Skip the tap when the URI cannot be resolved.</param>
    public static T WireTap<T>(this T steps, IExpression uri, bool copy = true, Action<Exchange>? onPrepare = null, int cacheSize = SendDynamicProcessor.DefaultCacheSize, bool ignoreInvalidEndpoint = false)
        where T : RouteSteps
    {
        steps.AddStep(new WireTapDefinition(uri)
        {
            Copy = copy,
            OnPrepare = onPrepare,
            CacheSize = cacheSize,
            IgnoreInvalidEndpoint = ignoreInvalidEndpoint,
        });
        return steps;
    }

    /// <summary>Wire taps to the URI returned by <paramref name="uri"/> for the tapped exchange.</summary>
    /// <param name="steps">The block to add to.</param>
    /// <param name="uri">Computes the URI.</param>
    /// <param name="copy">True (default) to tap a copy of the exchange; false to tap a new, empty exchange.</param>
    /// <param name="onPrepare">Optional callback to modify the tapped exchange before it is sent.</param>
    /// <param name="cacheSize">Maximum cached producers (default 1000; negative disables caching).</param>
    /// <param name="ignoreInvalidEndpoint">Skip the tap when the URI cannot be resolved.</param>
    public static T WireTap<T>(this T steps, Func<Exchange, string> uri, bool copy = true, Action<Exchange>? onPrepare = null, int cacheSize = SendDynamicProcessor.DefaultCacheSize, bool ignoreInvalidEndpoint = false)
        where T : RouteSteps => steps.WireTap(new FuncExpression(uri), copy, onPrepare, cacheSize, ignoreInvalidEndpoint);
}
