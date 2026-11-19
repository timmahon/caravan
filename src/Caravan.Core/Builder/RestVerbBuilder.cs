using Caravan.Model.Rest;

namespace Caravan.Builder;

/// <summary>
/// Configures a REST service (<see cref="VerbDefinition"/>):
/// <c>v =&gt; v.Produces("application/json").OutType&lt;Order&gt;().Param("id", RestParamType.Path).To("direct:getOrder")</c>.
/// </summary>
/// <param name="definition">The service.</param>
public sealed class RestVerbBuilder(VerbDefinition definition)
{
    /// <summary>The service being configured.</summary>
    public VerbDefinition Definition { get; } = definition;

    /// <summary>Sets the id (also the route id unless <see cref="RouteId"/> is set).</summary>
    /// <param name="id">The id.</param>
    /// <returns>This builder.</returns>
    public RestVerbBuilder Id(string id)
    {
        Definition.Id = id;
        return this;
    }

    /// <summary>Sets the id of the route created for the service.</summary>
    /// <param name="routeId">The route id.</param>
    /// <returns>This builder.</returns>
    public RestVerbBuilder RouteId(string routeId)
    {
        Definition.RouteId = routeId;
        return this;
    }

    /// <summary>Sets the description (the OpenAPI summary).</summary>
    /// <param name="description">The description.</param>
    /// <returns>This builder.</returns>
    public RestVerbBuilder Description(string description)
    {
        Definition.Description = description;
        return this;
    }

    /// <summary>Sets the media types accepted (comma-separated).</summary>
    /// <param name="consumes">The media types.</param>
    /// <returns>This builder.</returns>
    public RestVerbBuilder Consumes(string consumes)
    {
        Definition.Consumes = consumes;
        return this;
    }

    /// <summary>Sets the media types produced (comma-separated).</summary>
    /// <param name="produces">The media types.</param>
    /// <returns>This builder.</returns>
    public RestVerbBuilder Produces(string produces)
    {
        Definition.Produces = produces;
        return this;
    }

    /// <summary>Sets the type the request body is bound to (an array type for a list).</summary>
    /// <param name="type">The type.</param>
    /// <returns>This builder.</returns>
    public RestVerbBuilder Type(Type type)
    {
        Definition.Type = type;
        return this;
    }

    /// <summary>Sets the type the request body is bound to.</summary>
    /// <typeparam name="T">The type.</typeparam>
    /// <returns>This builder.</returns>
    public RestVerbBuilder Type<T>() => Type(typeof(T));

    /// <summary>Sets the type of the response body.</summary>
    /// <param name="type">The type.</param>
    /// <returns>This builder.</returns>
    public RestVerbBuilder OutType(Type type)
    {
        Definition.OutType = type;
        return this;
    }

    /// <summary>Sets the type of the response body.</summary>
    /// <typeparam name="T">The type.</typeparam>
    /// <returns>This builder.</returns>
    public RestVerbBuilder OutType<T>() => OutType(typeof(T));

    /// <summary>Sets the binding mode of the service.</summary>
    /// <param name="mode">The mode.</param>
    /// <returns>This builder.</returns>
    public RestVerbBuilder BindingMode(RestBindingMode mode)
    {
        Definition.BindingMode = mode;
        return this;
    }

    /// <summary>Sets whether responses with a code of 300 or more are not bound.</summary>
    /// <param name="skip">Whether to skip.</param>
    /// <returns>This builder.</returns>
    public RestVerbBuilder SkipBindingOnErrorCode(bool skip = true)
    {
        Definition.SkipBindingOnErrorCode = skip;
        return this;
    }

    /// <summary>Sets whether requests are validated.</summary>
    /// <param name="validate">Whether to validate.</param>
    /// <returns>This builder.</returns>
    public RestVerbBuilder ClientRequestValidation(bool validate = true)
    {
        Definition.ClientRequestValidation = validate;
        return this;
    }

    /// <summary>Sets whether CORS headers are added.</summary>
    /// <param name="enable">Whether to enable CORS.</param>
    /// <returns>This builder.</returns>
    public RestVerbBuilder EnableCors(bool enable = true)
    {
        Definition.EnableCors = enable;
        return this;
    }

    /// <summary>Sets whether the service is in the OpenAPI document.</summary>
    /// <param name="apiDocs">Whether to document it.</param>
    /// <returns>This builder.</returns>
    public RestVerbBuilder ApiDocs(bool apiDocs)
    {
        Definition.ApiDocs = apiDocs;
        return this;
    }

    /// <summary>Marks the service deprecated.</summary>
    /// <param name="deprecated">Whether it is deprecated.</param>
    /// <returns>This builder.</returns>
    public RestVerbBuilder Deprecated(bool deprecated = true)
    {
        Definition.Deprecated = deprecated;
        return this;
    }

    /// <summary>Disables the service (no route is created).</summary>
    /// <param name="disabled">Whether it is disabled.</param>
    /// <returns>This builder.</returns>
    public RestVerbBuilder Disabled(bool disabled = true)
    {
        Definition.Disabled = disabled;
        return this;
    }

    /// <summary>Sends requests to <paramref name="uri"/> (Camel 4's way: usually a <c>direct</c> route).</summary>
    /// <param name="uri">The endpoint URI.</param>
    /// <returns>This builder.</returns>
    public RestVerbBuilder To(string uri)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(uri);
        Definition.To = uri;
        return this;
    }

    /// <summary>Defines the service's steps inline: <c>.Route(r =&gt; r.SetBody(...).To(...))</c>.</summary>
    /// <param name="steps">Builds the steps.</param>
    /// <returns>This builder.</returns>
    public RestVerbBuilder Route(Action<RouteSteps> steps)
    {
        ArgumentNullException.ThrowIfNull(steps);
        steps(new RouteSteps(Definition.Outputs));
        return this;
    }

    /// <summary>Adds a parameter.</summary>
    /// <param name="name">The name.</param>
    /// <param name="type">Where it is.</param>
    /// <param name="configure">Further options.</param>
    /// <returns>This builder.</returns>
    public RestVerbBuilder Param(string name, RestParamType type, Action<RestParamBuilder>? configure = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var param = new RestParamDefinition { Name = name, Type = type };
        configure?.Invoke(new RestParamBuilder(param));
        Definition.Params.Add(param);
        return this;
    }

    /// <summary>Documents a response.</summary>
    /// <param name="code">The status code.</param>
    /// <param name="message">The description.</param>
    /// <param name="responseModel">The type of the body, if any.</param>
    /// <param name="configure">Further options (headers).</param>
    /// <returns>This builder.</returns>
    public RestVerbBuilder ResponseMessage(int code, string? message = null, Type? responseModel = null, Action<ResponseMessageBuilder>? configure = null) =>
        ResponseMessage(code.ToString(System.Globalization.CultureInfo.InvariantCulture), message, responseModel, configure);

    /// <summary>Documents a response (<paramref name="code"/> may be <c>default</c>).</summary>
    /// <param name="code">The status code.</param>
    /// <param name="message">The description.</param>
    /// <param name="responseModel">The type of the body, if any.</param>
    /// <param name="configure">Further options (headers).</param>
    /// <returns>This builder.</returns>
    public RestVerbBuilder ResponseMessage(string code, string? message = null, Type? responseModel = null, Action<ResponseMessageBuilder>? configure = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        var response = new ResponseMessageDefinition { Code = code, Message = message, ResponseModel = responseModel };
        configure?.Invoke(new ResponseMessageBuilder(response));
        Definition.ResponseMessages.Add(response);
        return this;
    }

    /// <summary>Requires a security scheme declared on the REST definition.</summary>
    /// <param name="key">The scheme's key.</param>
    /// <param name="scopes">The scopes required.</param>
    /// <returns>This builder.</returns>
    public RestVerbBuilder Security(string key, params string[] scopes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        var requirement = new RestSecurityRequirement { Key = key };
        requirement.Scopes.AddRange(scopes);
        Definition.Security.Add(requirement);
        return this;
    }
}
