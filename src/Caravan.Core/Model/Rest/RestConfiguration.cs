namespace Caravan.Model.Rest;

/// <summary>
/// The context-wide configuration of the REST DSL (Camel's <c>RestConfiguration</c> / <c>restConfiguration</c>), read with
/// <see cref="RestContextExtensions.GetRestConfiguration"/> and set with <c>RouteBuilder.RestConfiguration(c =&gt; ...)</c> or the YAML DSL's
/// <c>restConfiguration</c>. Values of REST definitions and verbs take precedence where both exist.
/// </summary>
public sealed class RestConfiguration
{
    /// <summary>The default value of <see cref="CorsHeaders"/>' <c>Access-Control-Allow-Origin</c>.</summary>
    public const string CorsAllowOrigin = "*";

    /// <summary>The default value of <c>Access-Control-Allow-Methods</c>.</summary>
    public const string CorsAllowMethods = "GET, HEAD, POST, PUT, DELETE, TRACE, OPTIONS, CONNECT, PATCH";

    /// <summary>The default value of <c>Access-Control-Max-Age</c>.</summary>
    public const string CorsMaxAge = "3600";

    /// <summary>The default value of <c>Access-Control-Allow-Headers</c>.</summary>
    public const string CorsAllowHeaders =
        "Origin, Accept, X-Requested-With, Content-Type, Access-Control-Request-Method, Access-Control-Request-Headers";

    /// <summary>
    /// The component serving REST services: the name of a component implementing <see cref="Spi.IRestConsumerFactory"/>, e.g.
    /// <c>platform-http</c>. When null, a single <see cref="Spi.IRestConsumerFactory"/> bean, then <c>platform-http</c>, is used.
    /// </summary>
    public string? Component { get; set; }

    /// <summary>The host name, for the OpenAPI document (the transport decides where it listens).</summary>
    public string? Host { get; set; }

    /// <summary>The port, for the OpenAPI document.</summary>
    public int? Port { get; set; }

    /// <summary>The scheme, for the OpenAPI document (default <c>http</c>).</summary>
    public string? Scheme { get; set; }

    /// <summary>A path prepended to every REST service, e.g. <c>/rest</c>.</summary>
    public string? ContextPath { get; set; }

    /// <summary>The path of the OpenAPI document under <see cref="ContextPath"/>, e.g. <c>/api-doc</c>; null serves none.</summary>
    public string? ApiContextPath { get; set; }

    /// <summary>OpenAPI <c>info</c> properties: <c>api.title</c>/<c>title</c>, <c>api.version</c>/<c>version</c>, <c>api.description</c>/<c>description</c>...</summary>
    public Dictionary<string, string> ApiProperties { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The binding mode (default <see cref="RestBindingMode.Off"/>).</summary>
    public RestBindingMode BindingMode { get; set; } = RestBindingMode.Off;

    /// <summary>Whether the response is not bound when its code is 300 or more (default true).</summary>
    public bool SkipBindingOnErrorCode { get; set; } = true;

    /// <summary>Whether requests are validated: 415 (content type not consumed), 406 (accept not produced), 400 (missing or invalid parameters or body) (default false).</summary>
    public bool ClientRequestValidation { get; set; }

    /// <summary>Whether CORS headers are added to responses and preflight requests answered (default false).</summary>
    public bool EnableCors { get; set; }

    /// <summary>CORS headers overriding the defaults (<see cref="CorsAllowOrigin"/>, ...).</summary>
    public Dictionary<string, string> CorsHeaders { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The data format binding JSON (default <c>json</c>).</summary>
    public string? JsonDataFormat { get; set; }

    /// <summary>The data format binding XML (default <c>xml</c>).</summary>
    public string? XmlDataFormat { get; set; }

    /// <summary>
    /// Properties set on the binding data formats: <c>name</c> for both, or prefixed <c>json.in.</c>, <c>json.out.</c>, <c>xml.in.</c>,
    /// <c>xml.out.</c> for one direction (e.g. <c>prettyPrint=true</c>, <c>json.out.include=NON_NULL</c>).
    /// </summary>
    public Dictionary<string, string> DataFormatProperties { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Options passed to the consumers the REST component creates (Camel's <c>endpointProperty</c>), e.g. <c>muteException=true</c>.</summary>
    public Dictionary<string, string> EndpointProperties { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Whether a verb sending to <c>direct:x</c> is merged with the route consuming from <c>direct:x</c> when both are defined by the same route
    /// builder and only that verb uses it (default true, as in Camel 4): one route per service instead of two.
    /// </summary>
    public bool InlineRoutes { get; set; } = true;

    /// <summary>The CORS headers to send: the defaults overridden by <see cref="CorsHeaders"/>.</summary>
    /// <returns>Header name to value.</returns>
    public IReadOnlyDictionary<string, string> GetCorsHeaders()
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Access-Control-Allow-Origin"] = CorsAllowOrigin,
            ["Access-Control-Allow-Methods"] = CorsAllowMethods,
            ["Access-Control-Allow-Headers"] = CorsAllowHeaders,
            ["Access-Control-Max-Age"] = CorsMaxAge,
        };
        foreach (var (name, value) in CorsHeaders)
        {
            headers[name] = value;
        }

        return headers;
    }

    /// <summary>Parses Camel's binding mode names: <c>off</c>, <c>auto</c>, <c>json</c>, <c>xml</c>, <c>json_xml</c> (case-insensitive; <c>JsonXml</c> too).</summary>
    /// <param name="text">The name.</param>
    /// <returns>The mode.</returns>
    /// <exception cref="ArgumentException">Another value.</exception>
    public static RestBindingMode ParseBindingMode(string text) => text.Trim().ToLowerInvariant() switch
    {
        "off" => RestBindingMode.Off,
        "auto" => RestBindingMode.Auto,
        "json" => RestBindingMode.Json,
        "xml" => RestBindingMode.Xml,
        "json_xml" or "jsonxml" or "json-xml" => RestBindingMode.JsonXml,
        _ => throw new ArgumentException($"Unknown REST binding mode '{text}': use off, auto, json, xml or json_xml"),
    };
}
