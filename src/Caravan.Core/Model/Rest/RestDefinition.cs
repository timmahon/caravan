namespace Caravan.Model.Rest;

/// <summary>
/// A REST API (Camel's <c>RestDefinition</c>): a base path, defaults for its services and the services themselves (<see cref="Verbs"/>).
/// Defined with <c>RouteBuilder.Rest("/api/orders", r =&gt; r.Get("/{id}", v =&gt; v.To("direct:get")))</c> or the YAML DSL's <c>rest</c>; each
/// verb becomes a route (see <see cref="VerbDefinition"/>). It is transport-agnostic: a component implementing
/// <see cref="Spi.IRestConsumerFactory"/> (e.g. <c>platform-http</c> on ASP.NET Core) serves it.
/// </summary>
public sealed class RestDefinition
{
    /// <summary>The id of the REST definition.</summary>
    public string? Id { get; set; }

    /// <summary>The base path of the services, e.g. <c>/api/orders</c>.</summary>
    public string? Path { get; set; }

    /// <summary>The OpenAPI tag of the services (defaults to the path in the OpenAPI document).</summary>
    public string? Tag { get; set; }

    /// <summary>A description.</summary>
    public string? Description { get; set; }

    /// <summary>The media types accepted by default (comma-separated).</summary>
    public string? Consumes { get; set; }

    /// <summary>The media types produced by default (comma-separated).</summary>
    public string? Produces { get; set; }

    /// <summary>The binding mode; null inherits the configuration's.</summary>
    public RestBindingMode? BindingMode { get; set; }

    /// <summary>Whether responses with a code of 300 or more are not bound; null inherits.</summary>
    public bool? SkipBindingOnErrorCode { get; set; }

    /// <summary>Whether requests are validated; null inherits.</summary>
    public bool? ClientRequestValidation { get; set; }

    /// <summary>Whether CORS headers are added; null inherits.</summary>
    public bool? EnableCors { get; set; }

    /// <summary>Whether the services are in the OpenAPI document (default true).</summary>
    public bool ApiDocs { get; set; } = true;

    /// <summary>Whether the whole REST definition is disabled.</summary>
    public bool Disabled { get; set; }

    /// <summary>The services.</summary>
    public List<VerbDefinition> Verbs { get; } = [];

    /// <summary>The security schemes declared by this definition.</summary>
    public List<RestSecurityDefinition> SecurityDefinitions { get; } = [];

    /// <summary>The security requirements of every service.</summary>
    public List<RestSecurityRequirement> SecurityRequirements { get; } = [];

    /// <summary>Adds a service.</summary>
    /// <param name="method">The HTTP method.</param>
    /// <param name="path">The path relative to <see cref="Path"/>, or null.</param>
    /// <returns>The service.</returns>
    public VerbDefinition AddVerb(string method, string? path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(method);
        var verb = new VerbDefinition { Method = method.ToLowerInvariant(), Path = path, Rest = this };
        Verbs.Add(verb);
        return verb;
    }

    /// <inheritdoc />
    public override string ToString() => $"Rest[{Path}]";
}
