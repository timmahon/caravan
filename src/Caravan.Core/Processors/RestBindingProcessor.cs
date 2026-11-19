using System.Text;
using Caravan.Model.Rest;
using Caravan.Spi;
using Caravan.Support;

namespace Caravan.Processors;

/// <summary>
/// The binding of a REST service (Camel's <c>RestBindingAdvice</c>), around the service's steps:
/// <list type="number">
/// <item>CORS: with CORS enabled an <c>OPTIONS</c> request is answered at once (200, CORS headers) and other responses get the CORS headers.</item>
/// <item>Query parameters with a default value are set as headers when missing.</item>
/// <item>Client request validation (when enabled): 415 when the <c>Content-Type</c> is not consumed, 406 when the <c>Accept</c> header accepts
/// nothing produced, 400 when a required body, query parameter or header is missing or a value is not allowed, 400 when the body cannot be
/// bound. The service's steps are then skipped.</item>
/// <item>Unmarshal: in <c>json</c>/<c>xml</c>/<c>json_xml</c>/<c>auto</c> mode a non-empty body is unmarshalled with the data format chosen by
/// the request's <c>Content-Type</c> (else <c>consumes</c>, else the mode; json first), into the verb's type.</item>
/// <item>Marshal: unless the exchange failed, or <c>skipBindingOnErrorCode</c> and <see cref="CaravanHeaders.HttpResponseCode"/> is 300 or
/// more, the body is marshalled in the format chosen by the request's <c>Accept</c> header (else the response's <c>Content-Type</c>, else
/// <c>produces</c>, else the request's format) and <c>Content-Type</c> is set. Bodies that are already text, bytes or streams are left alone.</item>
/// </list>
/// In <c>off</c> mode only CORS, defaults and validation apply, and <c>Content-Type</c> is set from <c>produces</c> when missing.
/// </summary>
public sealed class RestBindingProcessor : INavigableProcessor
{
    private const string Json = "json";
    private const string Xml = "xml";

    private readonly RestBindingSettings _settings;
    private readonly IProcessor _block;

    internal RestBindingProcessor(RestBindingSettings settings, IProcessor block)
    {
        _settings = settings;
        _block = block;
    }

    /// <summary>The binding mode.</summary>
    public RestBindingMode BindingMode => _settings.BindingMode;

    /// <inheritdoc />
    public IReadOnlyList<IProcessor> Next => [_block];

    /// <inheritdoc />
    public async ValueTask ProcessAsync(Exchange exchange)
    {
        var message = exchange.Message;
        if (_settings.CorsHeaders is { } cors && string.Equals(message.GetHeader(CaravanHeaders.HttpMethod) as string, "OPTIONS", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(_settings.Method, "OPTIONS", StringComparison.OrdinalIgnoreCase))
        {
            SetCors(message, cors);
            Reply(message, 200, null);
            return;
        }

        foreach (var param in _settings.Params)
        {
            if (param is { Type: RestParamType.Query, DefaultValue: { } defaultValue } && IsMissing(message.GetHeader(param.Name)))
            {
                message.SetHeader(param.Name, defaultValue);
            }
        }

        var accept = message.GetHeader(CaravanHeaders.Accept)?.ToString();
        if (_settings.ClientRequestValidation && Validate(exchange, accept) is { } rejected)
        {
            Reply(message, rejected.Code, rejected.Text);
            SetCors(message, _settings.CorsHeaders);
            return;
        }

        var requestContentType = message.GetHeader(CaravanHeaders.ContentType)?.ToString();
        string? requestFormat;
        try
        {
            requestFormat = await UnmarshalAsync(exchange).ConfigureAwait(false);
        }
        catch (Exception e) when (_settings.ClientRequestValidation && e is not OperationCanceledException)
        {
            Reply(message, 400, "Invalid request body: " + e.Message);
            SetCors(message, _settings.CorsHeaders);
            return;
        }

        await _block.ProcessAsync(exchange).ConfigureAwait(false);
        if (exchange.IsFailed)
        {
            return;
        }

        message = exchange.Message;
        SetCors(message, _settings.CorsHeaders);
        await MarshalAsync(exchange, accept, requestFormat, requestContentType).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public override string ToString() => $"RestBinding[{_settings.BindingMode}]";

    private static bool IsMissing(object? value) => value is null || (value is string s && s.Length == 0);

    private static void Reply(Message message, int code, string? text)
    {
        message.SetHeader(CaravanHeaders.HttpResponseCode, code);
        message.Body = text;
        if (text is null)
        {
            message.RemoveHeader(CaravanHeaders.ContentType);
        }
        else
        {
            message.SetHeader(CaravanHeaders.ContentType, "text/plain; charset=utf-8");
        }
    }

    private static void SetCors(Message message, IReadOnlyDictionary<string, string>? cors)
    {
        if (cors is null)
        {
            return;
        }

        foreach (var (name, value) in cors)
        {
            if (!message.HasHeader(name))
            {
                message.SetHeader(name, value);
            }
        }
    }

    private static bool AllowsJson(RestBindingMode mode) => mode is RestBindingMode.Auto or RestBindingMode.Json or RestBindingMode.JsonXml;

    private static bool AllowsXml(RestBindingMode mode) => mode is RestBindingMode.Auto or RestBindingMode.Xml or RestBindingMode.JsonXml;

    private static bool IsRaw(object? body) => body is null or string or byte[] or Stream or ReadOnlyMemory<byte> or Memory<byte>;

    private (int Code, string Text)? Validate(Exchange exchange, string? accept)
    {
        var message = exchange.Message;
        var contentType = message.GetHeader(CaravanHeaders.ContentType)?.ToString();
        if (_settings.Consumes is not null && !string.IsNullOrWhiteSpace(contentType) && !MediaTypeHelper.Matches(_settings.Consumes, contentType))
        {
            return (415, $"Unsupported Media Type: {contentType} (accepted: {_settings.Consumes})");
        }

        if (_settings.Produces is not null && !string.IsNullOrWhiteSpace(accept) && !MediaTypeHelper.Matches(_settings.Produces, accept))
        {
            return (406, $"Not Acceptable: {accept} (produced: {_settings.Produces})");
        }

        if (_settings.Params.Any(p => p is { Type: RestParamType.Body, Required: true }) && IsEmptyBody(exchange))
        {
            return (400, "The request body is required");
        }

        var missing = _settings.Params
            .Where(p => p is { Required: true, Type: RestParamType.Query or RestParamType.Header } && IsMissing(message.GetHeader(p.Name)))
            .Select(p => p.Name)
            .ToList();
        if (missing.Count > 0)
        {
            return (400, $"Some of the required query parameters or headers are missing: {string.Join(", ", missing)}");
        }

        foreach (var param in _settings.Params.Where(p => p.AllowableValues.Count > 0 && p.Type is not RestParamType.Body))
        {
            if (message.GetHeader(param.Name) is { } value && !IsMissing(value)
                && !param.AllowableValues.Contains(value.ToString() ?? string.Empty, StringComparer.Ordinal))
            {
                return (400, $"Invalid value '{value}' of parameter {param.Name}: allowed values are {string.Join(", ", param.AllowableValues)}");
            }
        }

        return null;
    }

    private static bool IsEmptyBody(Exchange exchange) => exchange.Message.Body switch
    {
        null => true,
        string s => string.IsNullOrWhiteSpace(s),
        byte[] b => b.Length == 0,
        IStreamCache cache => cache.Length == 0 || (cache.Length <= 4096 && string.IsNullOrWhiteSpace(exchange.Message.GetBody<string>())),
        _ => false,
    };

    /// <summary>Unmarshals the request body; returns the format chosen (json, xml) or null.</summary>
    private async ValueTask<string?> UnmarshalAsync(Exchange exchange)
    {
        var mode = _settings.BindingMode;
        if (mode == RestBindingMode.Off)
        {
            return null;
        }

        var message = exchange.Message;
        var contentType = message.GetHeader(CaravanHeaders.ContentType)?.ToString()?.ToLowerInvariant();
        var isXml = contentType?.Contains(Xml, StringComparison.Ordinal) == true;
        var isJson = contentType?.Contains(Json, StringComparison.Ordinal) == true;
        if (!isXml && !isJson && _settings.Consumes is { } consumes)
        {
            isXml = consumes.Contains(Xml, StringComparison.OrdinalIgnoreCase);
            isJson = consumes.Contains(Json, StringComparison.OrdinalIgnoreCase);
        }

        isXml &= AllowsXml(mode);
        isJson &= AllowsJson(mode);
        if (!isXml && !isJson)
        {
            isXml = AllowsXml(mode);
            isJson = AllowsJson(mode);
        }

        if (message.Body is null)
        {
            return isJson ? Json : Xml;
        }

        var text = message.GetBody<string>();
        if (string.IsNullOrWhiteSpace(text))
        {
            message.Body = null;
            return isJson ? Json : Xml;
        }

        if (isXml && isJson)
        {
            isXml = text.TrimStart().StartsWith('<');
            isJson = !isXml;
        }

        var dataFormat = isJson ? _settings.JsonUnmarshal : _settings.XmlUnmarshal;
        if (dataFormat is null)
        {
            message.Body = text;
            return isJson ? Json : Xml;
        }

        using var input = new MemoryStream(Encoding.UTF8.GetBytes(text));
        message.Body = await dataFormat.UnmarshalAsync(exchange, input).ConfigureAwait(false);
        return isJson ? Json : Xml;
    }

    private async ValueTask MarshalAsync(Exchange exchange, string? accept, string? requestFormat, string? requestContentType)
    {
        var message = exchange.Message;

        // The request's Content-Type header is still on the message unless the service set its own.
        var current = message.GetHeader(CaravanHeaders.ContentType)?.ToString();
        var contentType = current == requestContentType ? null : current?.ToLowerInvariant();
        if (contentType is null)
        {
            message.RemoveHeader(CaravanHeaders.ContentType);
        }

        if (_settings.SkipBindingOnErrorCode && message.GetHeader(CaravanHeaders.HttpResponseCode) is { } rawCode
            && int.TryParse(rawCode.ToString(), out var code) && code >= 300)
        {
            return;
        }

        var mode = _settings.BindingMode;

        if (mode == RestBindingMode.Off || message.Body is null)
        {
            EnsureContentType(message, null);
            return;
        }

        var isXml = false;
        var isJson = false;
        if (!string.IsNullOrWhiteSpace(accept) && !accept.Contains("*/*", StringComparison.Ordinal))
        {
            isXml = accept.Contains(Xml, StringComparison.OrdinalIgnoreCase);
            isJson = accept.Contains(Json, StringComparison.OrdinalIgnoreCase);
        }

        if (!isXml && !isJson && contentType is not null)
        {
            isXml = contentType.Contains(Xml, StringComparison.Ordinal);
            isJson = contentType.Contains(Json, StringComparison.Ordinal);
        }

        if (!isXml && !isJson && _settings.Produces is { } produces)
        {
            isXml = produces.Contains(Xml, StringComparison.OrdinalIgnoreCase);
            isJson = produces.Contains(Json, StringComparison.OrdinalIgnoreCase);
        }

        isXml &= AllowsXml(mode);
        isJson &= AllowsJson(mode);
        if (!isXml && !isJson)
        {
            isXml = AllowsXml(mode);
            isJson = AllowsJson(mode);
        }

        if (isXml && isJson)
        {
            isXml = requestFormat == Xml;
            isJson = !isXml;
        }

        if (IsRaw(message.Body) || message.Body is IStreamCache)
        {
            EnsureContentType(message, isJson ? Json : Xml);
            return;
        }

        var dataFormat = isJson ? _settings.JsonMarshal : _settings.XmlMarshal;
        if (dataFormat is null || (contentType is not null && !contentType.Contains(isJson ? Json : Xml, StringComparison.Ordinal)))
        {
            EnsureContentType(message, null);
            return;
        }

        using var output = new MemoryStream();
        await dataFormat.MarshalAsync(exchange, message.Body, output).ConfigureAwait(false);
        message.Body = output.ToArray();
        EnsureContentType(message, isJson ? Json : Xml);
    }

    private void EnsureContentType(Message message, string? format)
    {
        if (message.HasHeader(CaravanHeaders.ContentType))
        {
            return;
        }

        var produced = MediaTypeHelper.Split(_settings.Produces);
        var contentType = format switch
        {
            Json => produced.FirstOrDefault(p => p.Contains(Json, StringComparison.Ordinal)) ?? "application/json",
            Xml => produced.FirstOrDefault(p => p.Contains(Xml, StringComparison.Ordinal)) ?? "application/xml",
            _ => produced.Count == 1 ? produced[0] : null,
        };
        if (contentType is not null)
        {
            message.SetHeader(CaravanHeaders.ContentType, contentType);
        }
    }
}
