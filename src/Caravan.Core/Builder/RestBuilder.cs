using Caravan.Model.Rest;

namespace Caravan.Builder;

/// <summary>
/// Configures a REST API (<see cref="RestDefinition"/>) and its services:
/// <c>r =&gt; r.Tag("orders").Get("/{id}", v =&gt; v.To("direct:getOrder")).Post(v =&gt; v.Type&lt;Order&gt;().To("direct:createOrder"))</c>.
/// </summary>
/// <param name="definition">The REST definition.</param>
public sealed class RestBuilder(RestDefinition definition)
{
    /// <summary>The REST definition being configured.</summary>
    public RestDefinition Definition { get; } = definition;

    /// <summary>Sets the id.</summary>
    /// <param name="id">The id.</param>
    /// <returns>This builder.</returns>
    public RestBuilder Id(string id)
    {
        Definition.Id = id;
        return this;
    }

    /// <summary>Sets the OpenAPI tag.</summary>
    /// <param name="tag">The tag.</param>
    /// <returns>This builder.</returns>
    public RestBuilder Tag(string tag)
    {
        Definition.Tag = tag;
        return this;
    }

    /// <summary>Sets the description.</summary>
    /// <param name="description">The description.</param>
    /// <returns>This builder.</returns>
    public RestBuilder Description(string description)
    {
        Definition.Description = description;
        return this;
    }

    /// <summary>Sets the media types accepted by default.</summary>
    /// <param name="consumes">The media types (comma-separated).</param>
    /// <returns>This builder.</returns>
    public RestBuilder Consumes(string consumes)
    {
        Definition.Consumes = consumes;
        return this;
    }

    /// <summary>Sets the media types produced by default.</summary>
    /// <param name="produces">The media types (comma-separated).</param>
    /// <returns>This builder.</returns>
    public RestBuilder Produces(string produces)
    {
        Definition.Produces = produces;
        return this;
    }

    /// <summary>Sets the binding mode of the services.</summary>
    /// <param name="mode">The mode.</param>
    /// <returns>This builder.</returns>
    public RestBuilder BindingMode(RestBindingMode mode)
    {
        Definition.BindingMode = mode;
        return this;
    }

    /// <summary>Sets whether responses with a code of 300 or more are not bound.</summary>
    /// <param name="skip">Whether to skip.</param>
    /// <returns>This builder.</returns>
    public RestBuilder SkipBindingOnErrorCode(bool skip = true)
    {
        Definition.SkipBindingOnErrorCode = skip;
        return this;
    }

    /// <summary>Sets whether requests are validated.</summary>
    /// <param name="validate">Whether to validate.</param>
    /// <returns>This builder.</returns>
    public RestBuilder ClientRequestValidation(bool validate = true)
    {
        Definition.ClientRequestValidation = validate;
        return this;
    }

    /// <summary>Sets whether CORS headers are added.</summary>
    /// <param name="enable">Whether to enable CORS.</param>
    /// <returns>This builder.</returns>
    public RestBuilder EnableCors(bool enable = true)
    {
        Definition.EnableCors = enable;
        return this;
    }

    /// <summary>Sets whether the services are in the OpenAPI document.</summary>
    /// <param name="apiDocs">Whether to document them.</param>
    /// <returns>This builder.</returns>
    public RestBuilder ApiDocs(bool apiDocs)
    {
        Definition.ApiDocs = apiDocs;
        return this;
    }

    /// <summary>Disables the whole REST definition.</summary>
    /// <param name="disabled">Whether it is disabled.</param>
    /// <returns>This builder.</returns>
    public RestBuilder Disabled(bool disabled = true)
    {
        Definition.Disabled = disabled;
        return this;
    }

    /// <summary>Declares an HTTP basic authentication scheme.</summary>
    /// <param name="key">The key services refer to.</param>
    /// <param name="description">A description.</param>
    /// <returns>This builder.</returns>
    public RestBuilder BasicAuth(string key, string? description = null) =>
        AddSecurity(new RestSecurityDefinition { Key = key, Type = RestSecurityType.Basic, Description = description });

    /// <summary>Declares an HTTP bearer token scheme.</summary>
    /// <param name="key">The key services refer to.</param>
    /// <param name="format">The token format, e.g. <c>JWT</c>.</param>
    /// <param name="description">A description.</param>
    /// <returns>This builder.</returns>
    public RestBuilder BearerToken(string key, string? format = null, string? description = null) =>
        AddSecurity(new RestSecurityDefinition { Key = key, Type = RestSecurityType.Bearer, Format = format, Description = description });

    /// <summary>Declares an API key scheme.</summary>
    /// <param name="key">The key services refer to.</param>
    /// <param name="name">The name of the header, query parameter or cookie.</param>
    /// <param name="in">Where the key is: <c>header</c>, <c>query</c> or <c>cookie</c>.</param>
    /// <param name="description">A description.</param>
    /// <returns>This builder.</returns>
    public RestBuilder ApiKey(string key, string name, string @in = "header", string? description = null) =>
        AddSecurity(new RestSecurityDefinition { Key = key, Type = RestSecurityType.ApiKey, Name = name, In = @in, Description = description });

    /// <summary>Requires a security scheme for every service.</summary>
    /// <param name="key">The scheme's key.</param>
    /// <param name="scopes">The scopes required.</param>
    /// <returns>This builder.</returns>
    public RestBuilder Security(string key, params string[] scopes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        var requirement = new RestSecurityRequirement { Key = key };
        requirement.Scopes.AddRange(scopes);
        Definition.SecurityRequirements.Add(requirement);
        return this;
    }

    /// <summary>Adds a GET service on the base path.</summary>
    /// <param name="configure">Configures the service.</param>
    /// <returns>This builder.</returns>
    public RestBuilder Get(Action<RestVerbBuilder> configure) => Verb("get", null, configure);

    /// <summary>Adds a GET service.</summary>
    /// <param name="path">The path relative to the base path, with <c>{name}</c> parameters.</param>
    /// <param name="configure">Configures the service.</param>
    /// <returns>This builder.</returns>
    public RestBuilder Get(string path, Action<RestVerbBuilder> configure) => Verb("get", path, configure);

    /// <summary>Adds a POST service on the base path.</summary>
    /// <param name="configure">Configures the service.</param>
    /// <returns>This builder.</returns>
    public RestBuilder Post(Action<RestVerbBuilder> configure) => Verb("post", null, configure);

    /// <summary>Adds a POST service.</summary>
    /// <param name="path">The path relative to the base path.</param>
    /// <param name="configure">Configures the service.</param>
    /// <returns>This builder.</returns>
    public RestBuilder Post(string path, Action<RestVerbBuilder> configure) => Verb("post", path, configure);

    /// <summary>Adds a PUT service on the base path.</summary>
    /// <param name="configure">Configures the service.</param>
    /// <returns>This builder.</returns>
    public RestBuilder Put(Action<RestVerbBuilder> configure) => Verb("put", null, configure);

    /// <summary>Adds a PUT service.</summary>
    /// <param name="path">The path relative to the base path.</param>
    /// <param name="configure">Configures the service.</param>
    /// <returns>This builder.</returns>
    public RestBuilder Put(string path, Action<RestVerbBuilder> configure) => Verb("put", path, configure);

    /// <summary>Adds a PATCH service on the base path.</summary>
    /// <param name="configure">Configures the service.</param>
    /// <returns>This builder.</returns>
    public RestBuilder Patch(Action<RestVerbBuilder> configure) => Verb("patch", null, configure);

    /// <summary>Adds a PATCH service.</summary>
    /// <param name="path">The path relative to the base path.</param>
    /// <param name="configure">Configures the service.</param>
    /// <returns>This builder.</returns>
    public RestBuilder Patch(string path, Action<RestVerbBuilder> configure) => Verb("patch", path, configure);

    /// <summary>Adds a DELETE service on the base path.</summary>
    /// <param name="configure">Configures the service.</param>
    /// <returns>This builder.</returns>
    public RestBuilder Delete(Action<RestVerbBuilder> configure) => Verb("delete", null, configure);

    /// <summary>Adds a DELETE service.</summary>
    /// <param name="path">The path relative to the base path.</param>
    /// <param name="configure">Configures the service.</param>
    /// <returns>This builder.</returns>
    public RestBuilder Delete(string path, Action<RestVerbBuilder> configure) => Verb("delete", path, configure);

    /// <summary>Adds a HEAD service on the base path.</summary>
    /// <param name="configure">Configures the service.</param>
    /// <returns>This builder.</returns>
    public RestBuilder Head(Action<RestVerbBuilder> configure) => Verb("head", null, configure);

    /// <summary>Adds a HEAD service.</summary>
    /// <param name="path">The path relative to the base path.</param>
    /// <param name="configure">Configures the service.</param>
    /// <returns>This builder.</returns>
    public RestBuilder Head(string path, Action<RestVerbBuilder> configure) => Verb("head", path, configure);

    /// <summary>Adds an OPTIONS service on the base path.</summary>
    /// <param name="configure">Configures the service.</param>
    /// <returns>This builder.</returns>
    public RestBuilder Options(Action<RestVerbBuilder> configure) => Verb("options", null, configure);

    /// <summary>Adds an OPTIONS service.</summary>
    /// <param name="path">The path relative to the base path.</param>
    /// <param name="configure">Configures the service.</param>
    /// <returns>This builder.</returns>
    public RestBuilder Options(string path, Action<RestVerbBuilder> configure) => Verb("options", path, configure);

    /// <summary>Adds a service for any HTTP method.</summary>
    /// <param name="method">The HTTP method.</param>
    /// <param name="path">The path relative to the base path, or null.</param>
    /// <param name="configure">Configures the service.</param>
    /// <returns>This builder.</returns>
    public RestBuilder Verb(string method, string? path, Action<RestVerbBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        configure(new RestVerbBuilder(Definition.AddVerb(method, path)));
        return this;
    }

    private RestBuilder AddSecurity(RestSecurityDefinition definition)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(definition.Key);
        Definition.SecurityDefinitions.Add(definition);
        return this;
    }
}
