namespace Caravan.Model.Rest;

/// <summary>
/// A REST service: an HTTP method on a path of a <see cref="RestDefinition"/> (Camel's <c>VerbDefinition</c>: <c>get</c>, <c>post</c>...). It
/// becomes a route consuming from <c>rest:method:path:uriTemplate</c> whose single top-level node is a <see cref="RestBindingDefinition"/>
/// holding either a <c>to</c> (<see cref="To"/>, preferred by Camel 4) or the steps of an inline route (<see cref="Outputs"/>).
/// </summary>
public sealed class VerbDefinition
{
    /// <summary>The HTTP method in lower case (<c>get</c>, <c>post</c>, <c>put</c>, <c>patch</c>, <c>delete</c>, <c>head</c>, <c>options</c>).</summary>
    public string Method { get; set; } = "get";

    /// <summary>The path relative to the REST definition's path (Camel's <c>path</c>, the URI template), with <c>{name}</c> parameters; may be null.</summary>
    public string? Path { get; set; }

    /// <summary>The id of the service (used as route id when <see cref="RouteId"/> is not set).</summary>
    public string? Id { get; set; }

    /// <summary>The id of the route created for the service.</summary>
    public string? RouteId { get; set; }

    /// <summary>A description (the OpenAPI summary).</summary>
    public string? Description { get; set; }

    /// <summary>The media types accepted (comma-separated); overrides the REST definition's.</summary>
    public string? Consumes { get; set; }

    /// <summary>The media types produced (comma-separated); overrides the REST definition's.</summary>
    public string? Produces { get; set; }

    /// <summary>The type the request body is bound to (Camel's <c>type</c>; an array type such as <c>Order[]</c> for a list).</summary>
    public Type? Type { get; set; }

    /// <summary>The type of the response body (Camel's <c>outType</c>), documented in the OpenAPI document.</summary>
    public Type? OutType { get; set; }

    /// <summary>The binding mode; null inherits the REST definition's, then the configuration's.</summary>
    public RestBindingMode? BindingMode { get; set; }

    /// <summary>Whether the response is not bound when the response code is 300 or more; null inherits.</summary>
    public bool? SkipBindingOnErrorCode { get; set; }

    /// <summary>Whether the request is validated (415, 406, 400); null inherits.</summary>
    public bool? ClientRequestValidation { get; set; }

    /// <summary>Whether CORS headers are added to responses; null inherits.</summary>
    public bool? EnableCors { get; set; }

    /// <summary>Whether the service is in the OpenAPI document; null inherits (default true).</summary>
    public bool? ApiDocs { get; set; }

    /// <summary>Whether the service is marked deprecated in the OpenAPI document.</summary>
    public bool Deprecated { get; set; }

    /// <summary>Whether the service is disabled (no route is created).</summary>
    public bool Disabled { get; set; }

    /// <summary>The endpoint the request is sent to (Camel's <c>to</c>); null when the verb has inline <see cref="Outputs"/>.</summary>
    public string? To { get; set; }

    /// <summary>The steps of an inline route (a Caravan convenience; Camel 4 prefers <see cref="To"/>).</summary>
    public List<ProcessorDefinition> Outputs { get; } = [];

    /// <summary>The parameters.</summary>
    public List<RestParamDefinition> Params { get; } = [];

    /// <summary>The documented responses.</summary>
    public List<ResponseMessageDefinition> ResponseMessages { get; } = [];

    /// <summary>The security requirements of the service (in addition to the REST definition's).</summary>
    public List<RestSecurityRequirement> Security { get; } = [];

    /// <summary>The REST definition the verb belongs to.</summary>
    public RestDefinition? Rest { get; set; }

    /// <inheritdoc />
    public override string ToString() => $"{Method.ToUpperInvariant()} {Rest?.Path}{Path}";
}
