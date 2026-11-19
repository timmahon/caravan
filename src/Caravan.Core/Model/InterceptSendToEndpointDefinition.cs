using Caravan.Spi;

namespace Caravan.Model;

/// <summary>
/// The <c>interceptSendToEndpoint</c> EIP: <see cref="OutputDefinition.Outputs"/> run before an exchange is sent to an endpoint matching
/// <see cref="Uri"/>, then the exchange is sent to that endpoint (unless <see cref="SkipSendToOriginalEndpoint"/>), then to
/// <see cref="AfterUri"/> when set. Applies to every send in the context once a route using it is created: <c>to</c>, <c>toD</c>,
/// <c>recipientList</c>, <c>wireTap</c>, <c>enrich</c>, dead letter channels and producer templates, as Camel intercepts the endpoint's
/// producer. The header <see cref="CaravanHeaders.InterceptedEndpoint"/> holds the URI of the intercepted endpoint.
/// </summary>
/// <remarks>
/// When <see cref="When"/> is set the block runs only for exchanges it matches, and the original endpoint is skipped only for those.
/// A failure or <c>stop</c> in the block skips the original endpoint. The block runs inside the sender's node, so the sending route's
/// error handler handles its failures (a redelivery repeats the block). Sends made by the block itself are not intercepted again by
/// the same interceptor.
/// </remarks>
public sealed class InterceptSendToEndpointDefinition : OutputDefinition
{
    /// <summary>Creates an interceptor for endpoints matching <paramref name="uri"/>.</summary>
    /// <param name="uri">The endpoint URI pattern (exact, wildcard or regular expression).</param>
    public InterceptSendToEndpointDefinition(string uri)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(uri);
        Uri = uri;
    }

    /// <summary>The endpoint URI pattern (exact, wildcard such as <c>mock:*</c>, or regular expression).</summary>
    public string Uri { get; set; }

    /// <summary>When set, the block only runs (and the original endpoint is only skipped) for exchanges matching it.</summary>
    public IPredicate? When { get; set; }

    /// <summary>Whether the exchange is not sent to the original endpoint after the block (a detour).</summary>
    public bool SkipSendToOriginalEndpoint { get; set; }

    /// <summary>An endpoint the exchange is sent to after the original endpoint (to process its result), if any.</summary>
    public string? AfterUri { get; set; }

    /// <inheritdoc />
    public override string ShortName => "interceptSendToEndpoint";

    /// <inheritdoc />
    public override string Label => $"interceptSendToEndpoint[{Uri}]";

    /// <summary>Builds the block (the interceptor itself is registered with the context by the route).</summary>
    public override IProcessor CreateProcessor(RouteContext routeContext) => routeContext.CreateBlockProcessor(Outputs, this);
}
