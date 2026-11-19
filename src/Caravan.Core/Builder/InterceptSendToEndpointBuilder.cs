using Caravan.Model;

namespace Caravan.Builder;

/// <summary>
/// Builds an <c>interceptSendToEndpoint</c> block:
/// <c>InterceptSendToEndpoint("direct:payments", i =&gt; i.When(p).SkipSendToOriginalEndpoint().To("mock:payments"))</c>.
/// </summary>
public sealed class InterceptSendToEndpointBuilder : RouteSteps
{
    /// <summary>Creates a builder for <paramref name="definition"/>.</summary>
    /// <param name="definition">The interceptor being built.</param>
    /// <param name="route">The route it belongs to, if any.</param>
    public InterceptSendToEndpointBuilder(InterceptSendToEndpointDefinition definition, RouteDefinition? route = null)
        : base(definition.Outputs, route, definition)
    {
        Definition = definition;
    }

    /// <summary>The interceptor being built.</summary>
    public InterceptSendToEndpointDefinition Definition { get; }

    /// <summary>Runs the block (and skips the original endpoint, if configured) only for exchanges matching <paramref name="predicate"/>.</summary>
    /// <param name="predicate">The condition.</param>
    /// <returns>This builder.</returns>
    public InterceptSendToEndpointBuilder When(IPredicate predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        Definition.When = predicate;
        return this;
    }

    /// <summary>Runs the block (and skips the original endpoint, if configured) only for exchanges matching <paramref name="predicate"/>.</summary>
    /// <param name="predicate">The condition.</param>
    /// <returns>This builder.</returns>
    public InterceptSendToEndpointBuilder When(Func<Exchange, bool> predicate) => When(Pred.From(predicate));

    /// <summary>Does not send to the original endpoint after the block (a detour).</summary>
    /// <param name="skip">Whether to skip it.</param>
    /// <returns>This builder.</returns>
    public InterceptSendToEndpointBuilder SkipSendToOriginalEndpoint(bool skip = true)
    {
        Definition.SkipSendToOriginalEndpoint = skip;
        return this;
    }

    /// <summary>Sends to <paramref name="uri"/> after the original endpoint (to process its result).</summary>
    /// <param name="uri">The endpoint URI.</param>
    /// <returns>This builder.</returns>
    public InterceptSendToEndpointBuilder AfterUri(string uri)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(uri);
        Definition.AfterUri = uri;
        return this;
    }
}
