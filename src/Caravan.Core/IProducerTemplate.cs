namespace Caravan;

/// <summary>
/// Sends messages to endpoints from application code (Camel's <c>ProducerTemplate</c>).
/// <c>Send*</c> methods are fire-and-forget (InOnly), <c>Request*</c> methods are request/reply (InOut).
/// Methods returning a body throw <see cref="CaravanExecutionException"/> when routing failed;
/// methods returning the <see cref="Exchange"/> leave the exception on the exchange.
/// </summary>
public interface IProducerTemplate : IAsyncDisposable
{
    /// <summary>The endpoint used by overloads that take no URI.</summary>
    string? DefaultEndpointUri { get; set; }

    /// <summary>Sends an existing exchange; failures are set on the returned exchange instead of thrown.</summary>
    Task<Exchange> SendAsync(string uri, Exchange exchange);

    /// <summary>Creates an exchange with <paramref name="pattern"/>, lets <paramref name="configure"/> fill it and sends it; failures are set on the returned exchange.</summary>
    Task<Exchange> SendAsync(string uri, Action<Exchange> configure, ExchangePattern pattern = ExchangePattern.InOnly);

    /// <summary>Sends <paramref name="body"/> as an event message (<c>InOnly</c>).</summary>
    Task SendBodyAsync(string uri, object? body);

    /// <summary>Sends <paramref name="body"/> to the default endpoint.</summary>
    Task SendBodyAsync(object? body);

    /// <summary>Sends a body with one header (<c>InOnly</c>).</summary>
    Task SendBodyAndHeaderAsync(string uri, object? body, string header, object? headerValue);

    /// <summary>Sends a body with headers (<c>InOnly</c>).</summary>
    Task SendBodyAndHeadersAsync(string uri, object? body, IDictionary<string, object?> headers);

    /// <summary>Sends a body with one exchange property (<c>InOnly</c>).</summary>
    Task SendBodyAndPropertyAsync(string uri, object? body, string property, object? propertyValue);

    /// <summary>Sends <paramref name="body"/> as a request (<c>InOut</c>) and returns the reply body.</summary>
    Task<object?> RequestBodyAsync(string uri, object? body);

    /// <summary>Sends a request to the default endpoint and returns the reply body.</summary>
    Task<object?> RequestBodyAsync(object? body);

    /// <summary>Sends a request and returns the reply body converted to <typeparamref name="T"/>.</summary>
    Task<T?> RequestBodyAsync<T>(string uri, object? body);

    /// <summary>Sends a request with one header and returns the reply body.</summary>
    Task<object?> RequestBodyAndHeaderAsync(string uri, object? body, string header, object? headerValue);

    /// <summary>Sends a request with one header and returns the reply body converted to <typeparamref name="T"/>.</summary>
    Task<T?> RequestBodyAndHeaderAsync<T>(string uri, object? body, string header, object? headerValue);

    /// <summary>Sends a request with headers and returns the reply body.</summary>
    Task<object?> RequestBodyAndHeadersAsync(string uri, object? body, IDictionary<string, object?> headers);

    /// <summary>Sends a request with headers and returns the reply body converted to <typeparamref name="T"/>.</summary>
    Task<T?> RequestBodyAndHeadersAsync<T>(string uri, object? body, IDictionary<string, object?> headers);
}
