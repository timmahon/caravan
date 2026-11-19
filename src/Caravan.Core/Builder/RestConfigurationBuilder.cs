using Caravan.Model.Rest;

namespace Caravan.Builder;

/// <summary>
/// Configures the context's <see cref="RestConfiguration"/>:
/// <c>RestConfiguration(c =&gt; c.Component("platform-http").BindingMode(RestBindingMode.Json).ApiContextPath("/api-doc"))</c>.
/// </summary>
/// <param name="configuration">The configuration.</param>
public sealed class RestConfigurationBuilder(RestConfiguration configuration)
{
    /// <summary>The configuration being changed.</summary>
    public RestConfiguration Configuration { get; } = configuration;

    /// <summary>Sets the component serving REST services.</summary>
    /// <param name="component">The component name, e.g. <c>platform-http</c>.</param>
    /// <returns>This builder.</returns>
    public RestConfigurationBuilder Component(string component)
    {
        Configuration.Component = component;
        return this;
    }

    /// <summary>Sets the host (for the OpenAPI document).</summary>
    /// <param name="host">The host.</param>
    /// <returns>This builder.</returns>
    public RestConfigurationBuilder Host(string host)
    {
        Configuration.Host = host;
        return this;
    }

    /// <summary>Sets the port (for the OpenAPI document).</summary>
    /// <param name="port">The port.</param>
    /// <returns>This builder.</returns>
    public RestConfigurationBuilder Port(int port)
    {
        Configuration.Port = port;
        return this;
    }

    /// <summary>Sets the scheme (for the OpenAPI document).</summary>
    /// <param name="scheme">The scheme, e.g. <c>https</c>.</param>
    /// <returns>This builder.</returns>
    public RestConfigurationBuilder Scheme(string scheme)
    {
        Configuration.Scheme = scheme;
        return this;
    }

    /// <summary>Sets the path prepended to every REST service.</summary>
    /// <param name="contextPath">The path, e.g. <c>/rest</c>.</param>
    /// <returns>This builder.</returns>
    public RestConfigurationBuilder ContextPath(string contextPath)
    {
        Configuration.ContextPath = contextPath;
        return this;
    }

    /// <summary>Sets the path of the OpenAPI document (under the context path).</summary>
    /// <param name="apiContextPath">The path, e.g. <c>/api-doc</c>.</param>
    /// <returns>This builder.</returns>
    public RestConfigurationBuilder ApiContextPath(string apiContextPath)
    {
        Configuration.ApiContextPath = apiContextPath;
        return this;
    }

    /// <summary>Sets an OpenAPI property (<c>api.title</c>, <c>api.version</c>, <c>api.description</c>...).</summary>
    /// <param name="key">The key.</param>
    /// <param name="value">The value.</param>
    /// <returns>This builder.</returns>
    public RestConfigurationBuilder ApiProperty(string key, string value)
    {
        Configuration.ApiProperties[key] = value;
        return this;
    }

    /// <summary>Sets the binding mode.</summary>
    /// <param name="mode">The mode.</param>
    /// <returns>This builder.</returns>
    public RestConfigurationBuilder BindingMode(RestBindingMode mode)
    {
        Configuration.BindingMode = mode;
        return this;
    }

    /// <summary>Sets whether responses with a code of 300 or more are not bound.</summary>
    /// <param name="skip">Whether to skip.</param>
    /// <returns>This builder.</returns>
    public RestConfigurationBuilder SkipBindingOnErrorCode(bool skip)
    {
        Configuration.SkipBindingOnErrorCode = skip;
        return this;
    }

    /// <summary>Sets whether requests are validated.</summary>
    /// <param name="validate">Whether to validate.</param>
    /// <returns>This builder.</returns>
    public RestConfigurationBuilder ClientRequestValidation(bool validate = true)
    {
        Configuration.ClientRequestValidation = validate;
        return this;
    }

    /// <summary>Sets whether CORS headers are added.</summary>
    /// <param name="enable">Whether to enable CORS.</param>
    /// <returns>This builder.</returns>
    public RestConfigurationBuilder EnableCors(bool enable = true)
    {
        Configuration.EnableCors = enable;
        return this;
    }

    /// <summary>Overrides a CORS header.</summary>
    /// <param name="name">The header name.</param>
    /// <param name="value">The value.</param>
    /// <returns>This builder.</returns>
    public RestConfigurationBuilder CorsHeader(string name, string value)
    {
        Configuration.CorsHeaders[name] = value;
        return this;
    }

    /// <summary>Sets the data format binding JSON.</summary>
    /// <param name="name">The data format name.</param>
    /// <returns>This builder.</returns>
    public RestConfigurationBuilder JsonDataFormat(string name)
    {
        Configuration.JsonDataFormat = name;
        return this;
    }

    /// <summary>Sets the data format binding XML.</summary>
    /// <param name="name">The data format name.</param>
    /// <returns>This builder.</returns>
    public RestConfigurationBuilder XmlDataFormat(string name)
    {
        Configuration.XmlDataFormat = name;
        return this;
    }

    /// <summary>Sets a property of the binding data formats (see <see cref="RestConfiguration.DataFormatProperties"/>).</summary>
    /// <param name="key">The property, optionally prefixed with <c>json.in.</c>, <c>json.out.</c>, <c>xml.in.</c> or <c>xml.out.</c>.</param>
    /// <param name="value">The value.</param>
    /// <returns>This builder.</returns>
    public RestConfigurationBuilder DataFormatProperty(string key, string value)
    {
        Configuration.DataFormatProperties[key] = value;
        return this;
    }

    /// <summary>Sets an option of the consumers serving REST services.</summary>
    /// <param name="key">The option.</param>
    /// <param name="value">The value.</param>
    /// <returns>This builder.</returns>
    public RestConfigurationBuilder EndpointProperty(string key, string value)
    {
        Configuration.EndpointProperties[key] = value;
        return this;
    }

    /// <summary>Sets whether <c>direct</c> routes are inlined into REST services.</summary>
    /// <param name="inline">Whether to inline.</param>
    /// <returns>This builder.</returns>
    public RestConfigurationBuilder InlineRoutes(bool inline)
    {
        Configuration.InlineRoutes = inline;
        return this;
    }
}
