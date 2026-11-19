using System.Collections.Concurrent;
using Caravan.Spi;

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

    Task<Exchange> SendAsync(string uri, Exchange exchange);

    Task<Exchange> SendAsync(string uri, Action<Exchange> configure, ExchangePattern pattern = ExchangePattern.InOnly);

    Task SendBodyAsync(string uri, object? body);

    Task SendBodyAsync(object? body);

    Task SendBodyAndHeaderAsync(string uri, object? body, string header, object? headerValue);

    Task SendBodyAndHeadersAsync(string uri, object? body, IDictionary<string, object?> headers);

    Task SendBodyAndPropertyAsync(string uri, object? body, string property, object? propertyValue);

    Task<object?> RequestBodyAsync(string uri, object? body);

    Task<object?> RequestBodyAsync(object? body);

    Task<T?> RequestBodyAsync<T>(string uri, object? body);

    Task<object?> RequestBodyAndHeaderAsync(string uri, object? body, string header, object? headerValue);

    Task<T?> RequestBodyAndHeaderAsync<T>(string uri, object? body, string header, object? headerValue);

    Task<object?> RequestBodyAndHeadersAsync(string uri, object? body, IDictionary<string, object?> headers);

    Task<T?> RequestBodyAndHeadersAsync<T>(string uri, object? body, IDictionary<string, object?> headers);
}

internal sealed class ProducerTemplate(ICaravanContext context) : IProducerTemplate
{
    private readonly ConcurrentDictionary<IEndpoint, Lazy<Task<IProducer>>> _producers = new();

    public string? DefaultEndpointUri { get; set; }

    public async Task<Exchange> SendAsync(string uri, Exchange exchange)
    {
        var endpoint = context.GetEndpoint(uri);
        var producer = await GetProducerAsync(endpoint).ConfigureAwait(false);
        if (exchange.CancellationToken == default)
        {
            exchange.CancellationToken = context.ShutdownToken;
        }

        try
        {
            await producer.ProcessAsync(exchange).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            exchange.Exception = e;
        }

        return exchange;
    }

    public Task<Exchange> SendAsync(string uri, Action<Exchange> configure, ExchangePattern pattern = ExchangePattern.InOnly)
    {
        var exchange = new Exchange(context, pattern);
        configure(exchange);
        return SendAsync(uri, exchange);
    }

    public Task SendBodyAsync(string uri, object? body) => SendAndCheck(uri, ExchangePattern.InOnly, body, null);

    public Task SendBodyAsync(object? body) => SendBodyAsync(RequireDefault(), body);

    public Task SendBodyAndHeaderAsync(string uri, object? body, string header, object? headerValue) =>
        SendAndCheck(uri, ExchangePattern.InOnly, body, e => e.Message.SetHeader(header, headerValue));

    public Task SendBodyAndHeadersAsync(string uri, object? body, IDictionary<string, object?> headers) =>
        SendAndCheck(uri, ExchangePattern.InOnly, body, e => SetHeaders(e, headers));

    public Task SendBodyAndPropertyAsync(string uri, object? body, string property, object? propertyValue) =>
        SendAndCheck(uri, ExchangePattern.InOnly, body, e => e.SetProperty(property, propertyValue));

    public async Task<object?> RequestBodyAsync(string uri, object? body) =>
        (await SendAndCheck(uri, ExchangePattern.InOut, body, null).ConfigureAwait(false)).Message.Body;

    public Task<object?> RequestBodyAsync(object? body) => RequestBodyAsync(RequireDefault(), body);

    public async Task<T?> RequestBodyAsync<T>(string uri, object? body) =>
        (await SendAndCheck(uri, ExchangePattern.InOut, body, null).ConfigureAwait(false)).Message.GetBody<T>();

    public async Task<object?> RequestBodyAndHeaderAsync(string uri, object? body, string header, object? headerValue) =>
        (await SendAndCheck(uri, ExchangePattern.InOut, body, e => e.Message.SetHeader(header, headerValue)).ConfigureAwait(false)).Message.Body;

    public async Task<T?> RequestBodyAndHeaderAsync<T>(string uri, object? body, string header, object? headerValue) =>
        (await SendAndCheck(uri, ExchangePattern.InOut, body, e => e.Message.SetHeader(header, headerValue)).ConfigureAwait(false)).Message.GetBody<T>();

    public async Task<object?> RequestBodyAndHeadersAsync(string uri, object? body, IDictionary<string, object?> headers) =>
        (await SendAndCheck(uri, ExchangePattern.InOut, body, e => SetHeaders(e, headers)).ConfigureAwait(false)).Message.Body;

    public async Task<T?> RequestBodyAndHeadersAsync<T>(string uri, object? body, IDictionary<string, object?> headers) =>
        (await SendAndCheck(uri, ExchangePattern.InOut, body, e => SetHeaders(e, headers)).ConfigureAwait(false)).Message.GetBody<T>();

    public async ValueTask DisposeAsync()
    {
        foreach (var lazy in _producers.Values)
        {
            if (lazy.IsValueCreated)
            {
                try
                {
                    var producer = await lazy.Value.ConfigureAwait(false);
                    await producer.StopAsync().ConfigureAwait(false);
                }
                catch
                {
                    // ignore
                }
            }
        }

        _producers.Clear();
    }

    private async Task<Exchange> SendAndCheck(string uri, ExchangePattern pattern, object? body, Action<Exchange>? configure)
    {
        var exchange = new Exchange(context, pattern);
        exchange.Message.Body = body;
        configure?.Invoke(exchange);
        await SendAsync(uri, exchange).ConfigureAwait(false);
        if (exchange.Exception is { } e)
        {
            throw new CaravanExecutionException($"Exception occurred during execution on the exchange: {e.Message}", exchange, e);
        }

        return exchange;
    }

    private Task<IProducer> GetProducerAsync(IEndpoint endpoint) =>
        _producers.GetOrAdd(endpoint, ep => new Lazy<Task<IProducer>>(async () =>
        {
            await ep.StartAsync().ConfigureAwait(false);
            var producer = ep.CreateProducer();
            await producer.StartAsync().ConfigureAwait(false);
            return producer;
        })).Value;

    private string RequireDefault() =>
        DefaultEndpointUri ?? throw new InvalidOperationException("No default endpoint configured on the producer template");

    private static void SetHeaders(Exchange exchange, IDictionary<string, object?> headers)
    {
        foreach (var (k, v) in headers)
        {
            exchange.Message.SetHeader(k, v);
        }
    }
}
