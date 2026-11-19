using Caravan.Spi;
using Caravan.Support;
using Microsoft.Extensions.Logging;

namespace Caravan.Processors;

/// <summary>
/// The <c>routingSlip</c> EIP (Camel's <c>RoutingSlip</c>): evaluates <see cref="Expression"/> once to get the slip (URIs separated by
/// <see cref="UriDelimiter"/>, a sequence of URIs or endpoints, or an endpoint) and sends the exchange to each endpoint in turn, as a
/// pipeline: the output of each step is the input of the next. <see cref="ExchangePropertyKeys.SlipEndpoint"/> holds the endpoint being
/// sent to. The slip ends early when the exchange fails or is stopped.
/// </summary>
/// <remarks>
/// Each send is wrapped with the route's error handler, so a failing step is redelivered on its own. Endpoints that cannot be resolved fail
/// the exchange unless <see cref="IgnoreInvalidEndpoints"/>. Producers are cached in a <see cref="ProducerCache"/>.
/// </remarks>
public class RoutingSlipProcessor : ServiceSupport, IProcessor
{
    private readonly Func<IProcessor, IProcessor>? _wrap;

    /// <summary>Creates the processor.</summary>
    /// <param name="expression">Computes the slip.</param>
    /// <param name="uriDelimiter">Splits a string slip (default <c>,</c>); null to not split.</param>
    /// <param name="ignoreInvalidEndpoints">Skips endpoints that cannot be resolved instead of failing.</param>
    /// <param name="cacheSize">Maximum cached producers; 0 uses the default, negative disables caching.</param>
    /// <param name="wrapStep">Wraps the sender of each step (with error handling).</param>
    public RoutingSlipProcessor(
        IExpression expression,
        string? uriDelimiter = ",",
        bool ignoreInvalidEndpoints = false,
        int cacheSize = ProducerCache.DefaultCacheSize,
        Func<IProcessor, IProcessor>? wrapStep = null)
    {
        Expression = expression;
        UriDelimiter = string.IsNullOrEmpty(uriDelimiter) ? null : uriDelimiter;
        IgnoreInvalidEndpoints = ignoreInvalidEndpoints;
        ProducerCache = new ProducerCache(null, cacheSize);
        _wrap = wrapStep;
    }

    /// <summary>Computes the slip.</summary>
    public IExpression Expression { get; }

    /// <summary>Splits a string slip; null when strings are not split.</summary>
    public string? UriDelimiter { get; }

    /// <summary>Whether endpoints that cannot be resolved are skipped.</summary>
    public bool IgnoreInvalidEndpoints { get; }

    /// <summary>The producers used to send to the endpoints.</summary>
    public ProducerCache ProducerCache { get; }

    /// <inheritdoc />
    public async ValueTask ProcessAsync(Exchange exchange)
    {
        foreach (var value in GetSlip(exchange))
        {
            exchange.CancellationToken.ThrowIfCancellationRequested();
            if (value is null || (value is string text && string.IsNullOrWhiteSpace(text)))
            {
                continue;
            }

            IEndpoint endpoint;
            try
            {
                endpoint = ProducerCache.ResolveEndpoint(exchange, value);
            }
            catch (Exception e) when (IgnoreInvalidEndpoints)
            {
                exchange.Context.LoggerFactory.CreateLogger(GetType())
                    .LogDebug(e, "Ignoring invalid endpoint {Uri} for exchange {ExchangeId}", value, exchange.ExchangeId);
                continue;
            }

            exchange.SetProperty(ExchangePropertyKeys.SlipEndpoint, endpoint.EndpointUri);
            IProcessor sender = new SlipSender(ProducerCache, endpoint);
            if (_wrap is not null)
            {
                sender = _wrap(sender);
            }

            try
            {
                await sender.ProcessAsync(exchange).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                exchange.Exception = e;
            }

            if (!Pipeline.ContinueProcessing(exchange))
            {
                return;
            }
        }
    }

    /// <inheritdoc />
    public override string ToString() => $"RoutingSlip[{Expression}]";

    /// <summary>
    /// The endpoints (URIs or <see cref="IEndpoint"/>s) to send <paramref name="exchange"/> to, enumerated lazily while the exchange is
    /// routed: each is sent to before the next one is requested. The routing slip evaluates its expression once.
    /// </summary>
    /// <param name="exchange">The exchange.</param>
    /// <returns>The endpoints.</returns>
    protected virtual IEnumerable<object?> GetSlip(Exchange exchange) => SplitIterator.Create(Expression.Evaluate(exchange), UriDelimiter);

    /// <summary>Stops the cached producers.</summary>
    protected override Task DoStopAsync(CancellationToken cancellationToken) => ProducerCache.StopAsync(cancellationToken);

    private sealed class SlipSender(ProducerCache cache, IEndpoint endpoint) : IProcessor
    {
        public ValueTask ProcessAsync(Exchange exchange) => cache.SendAsync(endpoint, exchange);

        public override string ToString() => $"SendTo[{endpoint.EndpointUri}]";
    }
}
