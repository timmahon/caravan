namespace Caravan.Spi;

/// <summary>
/// Intercepts sends to endpoints, whoever sends (routes, EIPs, producer templates): Camel's endpoint interception used by
/// <c>interceptSendToEndpoint</c> and by mocking endpoints in tests. Register instances with <see cref="SendToEndpointInterceptors"/>;
/// they take effect at once, also for producers created earlier.
/// </summary>
public interface ISendToEndpointInterceptor
{
    /// <summary>Whether sends to <paramref name="endpoint"/> are intercepted (the result may be cached per endpoint).</summary>
    /// <param name="endpoint">The endpoint sent to.</param>
    /// <returns>Whether to intercept.</returns>
    bool Matches(IEndpoint endpoint);

    /// <summary>
    /// Handles a send of <paramref name="exchange"/> to <paramref name="endpoint"/>; call <paramref name="next"/> to send it on (to the next
    /// interceptor, then the endpoint's producer), or do not to skip the endpoint.
    /// </summary>
    /// <param name="exchange">The exchange being sent.</param>
    /// <param name="endpoint">The endpoint it is sent to.</param>
    /// <param name="next">Sends to the original endpoint (through the remaining interceptors).</param>
    /// <returns>A task completing when the send is done.</returns>
    ValueTask SendAsync(Exchange exchange, IEndpoint endpoint, IProcessor next);
}
