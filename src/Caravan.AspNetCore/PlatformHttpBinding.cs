using System.Globalization;
using System.Text;
using Caravan.Spi;
using Caravan.Support;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;

namespace Caravan.AspNetCore;

/// <summary>Maps HTTP requests to exchanges and exchanges to HTTP responses (Camel's <c>PlatformHttpBinding</c>).</summary>
internal static class PlatformHttpBinding
{
    /// <summary>Handles a request for <paramref name="consumer"/>.</summary>
    public static async Task HandleAsync(HttpContext http, PlatformHttpConsumer consumer)
    {
        var endpoint = consumer.PlatformHttpEndpoint;
        if (!consumer.IsStarted)
        {
            http.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        var request = http.Request;
        if (endpoint.Consumes is { } consumes && !string.IsNullOrEmpty(request.ContentType) && !MediaTypeHelper.Matches(consumes, request.ContentType))
        {
            http.Response.StatusCode = StatusCodes.Status415UnsupportedMediaType;
            return;
        }

        if (endpoint.Produces is { } produces && request.Headers.Accept.Count > 0 && !MediaTypeHelper.Matches(produces, request.Headers.Accept.ToString()))
        {
            http.Response.StatusCode = StatusCodes.Status406NotAcceptable;
            return;
        }

        var context = endpoint.Context;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(http.RequestAborted, context.ShutdownToken);
        var exchange = endpoint.CreateExchange(ExchangePattern.InOut);
        exchange.CancellationToken = cancellation.Token;
        try
        {
            await PopulateAsync(http, exchange, endpoint).ConfigureAwait(false);
            await consumer.Processor.ProcessAsync(exchange).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (http.RequestAborted.IsCancellationRequested)
        {
            return;
        }
        catch (Exception e)
        {
            exchange.Exception ??= e;
        }

        if (http.RequestAborted.IsCancellationRequested)
        {
            return;
        }

        await WriteResponseAsync(http, exchange, endpoint).ConfigureAwait(false);
    }

    /// <summary>Answers a CORS preflight request.</summary>
    public static Task PreflightAsync(HttpContext http, IReadOnlyDictionary<string, string> headers)
    {
        foreach (var (name, value) in headers)
        {
            http.Response.Headers[name] = value;
        }

        http.Response.StatusCode = StatusCodes.Status200OK;
        return Task.CompletedTask;
    }

    private static async Task PopulateAsync(HttpContext http, Exchange exchange, PlatformHttpEndpoint endpoint)
    {
        var request = http.Request;
        var message = exchange.Message;
        var filter = endpoint.HeaderFilterStrategy;
        foreach (var (name, values) in request.Headers)
        {
            var value = ToValue(values);
            if (!filter.ApplyFilterToExternalHeaders(name, value, exchange))
            {
                message.SetHeader(name, value);
            }
        }

        foreach (var (name, values) in request.Query)
        {
            var value = ToValue(values);
            if (!filter.ApplyFilterToExternalHeaders(name, value, exchange))
            {
                message.SetHeader(name, value);
            }
        }

        if (request.HasFormContentType)
        {
            request.EnableBuffering();
            var form = await request.ReadFormAsync(exchange.CancellationToken).ConfigureAwait(false);
            foreach (var (name, values) in form)
            {
                var value = ToValue(values);
                if (!filter.ApplyFilterToExternalHeaders(name, value, exchange))
                {
                    message.SetHeader(name, value);
                }
            }

            request.Body.Position = 0;
        }

        string? remaining = null;
        foreach (var (name, value) in request.RouteValues)
        {
            if (name == PlatformHttpEngine.RemainingPathValue)
            {
                remaining = value?.ToString();
            }
            else if (!filter.ApplyFilterToExternalHeaders(name, value, exchange))
            {
                message.SetHeader(name, value?.ToString());
            }
        }

        var path = request.PathBase.Add(request.Path).Value ?? "/";
        message.SetHeader(CaravanHeaders.HttpMethod, request.Method);
        message.SetHeader(CaravanHeaders.HttpUri, path);
        message.SetHeader(CaravanHeaders.HttpUrl, request.GetDisplayUrl());
        message.SetHeader(CaravanHeaders.HttpPath, endpoint.MatchOnUriPrefix ? "/" + remaining : request.Path.Value ?? "/");
        if (request.QueryString.HasValue)
        {
            var raw = request.QueryString.Value![1..];
            message.SetHeader(CaravanHeaders.HttpRawQuery, raw);
            message.SetHeader(CaravanHeaders.HttpQuery, Uri.UnescapeDataString(raw.Replace('+', ' ')));
        }

        message.Body = await ReadBodyAsync(http, exchange).ConfigureAwait(false);
    }

    private static async Task<object?> ReadBodyAsync(HttpContext http, Exchange exchange)
    {
        var request = http.Request;
        if (request.ContentLength == 0)
        {
            return null;
        }

        if (!exchange.Context.StreamCaching.Enabled)
        {
            return request.ContentLength is null && !request.Headers.TransferEncoding.Any() ? null : request.Body;
        }

        var cache = await exchange.Context.StreamCaching.CacheAsync(exchange, new NonClosingStream(request.Body)).ConfigureAwait(false);
        if (cache.Length == 0)
        {
            if (cache is IDisposable disposable)
            {
                disposable.Dispose();
            }

            return null;
        }

        return cache;
    }

    private static object? ToValue(StringValues values) => values.Count switch
    {
        0 => null,
        1 => values[0],
        _ => values.ToList(),
    };

    private static async Task WriteResponseAsync(HttpContext http, Exchange exchange, PlatformHttpEndpoint endpoint)
    {
        var response = http.Response;
        var message = exchange.Message;
        if (exchange.Exception is { } exception)
        {
            endpoint.Context.LoggerFactory.CreateLogger(typeof(PlatformHttpConsumer).FullName!)
                .LogDebug(exception, "Exchange {ExchangeId} failed: answering with an error status", exchange.ExchangeId);
            response.StatusCode = endpoint.PlatformHttpComponent.StatusCodeOf(exception);
            if (!endpoint.MuteException)
            {
                response.ContentType = "text/plain; charset=utf-8";
                await response.WriteAsync(exception.ToString(), http.RequestAborted).ConfigureAwait(false);
            }

            return;
        }

        var body = message.Body;
        response.StatusCode = StatusCode(message, body);
        if (message.GetHeader(CaravanHeaders.HttpResponseText) is { } reason && http.Features.Get<IHttpResponseFeature>() is { } feature)
        {
            feature.ReasonPhrase = reason.ToString();
        }

        var filter = endpoint.HeaderFilterStrategy;
        foreach (var (name, value) in message.Headers)
        {
            if (filter.ApplyFilterToCaravanHeaders(name, value, exchange))
            {
                continue;
            }

            response.Headers[name] = value switch
            {
                string s => s,
                IEnumerable<string> strings => new StringValues([.. strings]),
                System.Collections.IEnumerable items => new StringValues([.. items.Cast<object?>().Select(Text)]),
                _ => Text(value),
            };
        }

        var contentType = message.GetHeader(CaravanHeaders.ContentType)?.ToString();
        if (contentType is null && body is not null && MediaTypeHelper.Split(endpoint.Produces ?? endpoint.RestProduces) is [var single])
        {
            contentType = single;
        }

        if (contentType is not null)
        {
            response.ContentType = contentType;
        }

        if (body is null || HttpMethods.IsHead(http.Request.Method) || response.StatusCode == StatusCodes.Status204NoContent)
        {
            return;
        }

        var token = http.RequestAborted;
        switch (body)
        {
            case string text:
                {
                    var bytes = Encoding.UTF8.GetBytes(text);
                    response.ContentLength = bytes.Length;
                    await response.Body.WriteAsync(bytes, token).ConfigureAwait(false);
                    break;
                }

            case byte[] bytes:
                response.ContentLength = bytes.Length;
                await response.Body.WriteAsync(bytes, token).ConfigureAwait(false);
                break;
            case ReadOnlyMemory<byte> memory:
                response.ContentLength = memory.Length;
                await response.Body.WriteAsync(memory, token).ConfigureAwait(false);
                break;
            case IStreamCache cache:
                response.ContentLength = cache.Length;
                await cache.WriteToAsync(response.Body, token).ConfigureAwait(false);
                break;
            case Stream stream:
                await using (stream.ConfigureAwait(false))
                {
                    await stream.CopyToAsync(response.Body, token).ConfigureAwait(false);
                }

                break;
            default:
                {
                    var converted = exchange.Context.TypeConverter.TryConvert(typeof(byte[]), exchange, body, out var result) && result is byte[] b
                        ? b
                        : Encoding.UTF8.GetBytes(Text(body));
                    response.ContentLength = converted.Length;
                    await response.Body.WriteAsync(converted, token).ConfigureAwait(false);
                    break;
                }
        }
    }

    /// <summary>The status: the response code header, else 204 for an empty body, else 200 (Camel's <c>determineResponseCode</c>).</summary>
    private static int StatusCode(Message message, object? body)
    {
        if (message.GetHeader(CaravanHeaders.HttpResponseCode) is { } raw
            && int.TryParse(Convert.ToString(raw, CultureInfo.InvariantCulture), NumberStyles.Integer, CultureInfo.InvariantCulture, out var code))
        {
            return code;
        }

        return body is null || (body is string s && s.Trim().Length == 0) ? StatusCodes.Status204NoContent : StatusCodes.Status200OK;
    }

    private static string Text(object? value) => value switch
    {
        null => string.Empty,
        DateTimeOffset date => date.ToString("R", CultureInfo.InvariantCulture),
        DateTime date => date.ToUniversalTime().ToString("R", CultureInfo.InvariantCulture),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty,
    };
}
