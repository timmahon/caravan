using System.Globalization;
using System.Text;
using Caravan.Model.Rest;
using Caravan.Spi;
using Caravan.Support;

namespace Caravan.AspNetCore;

/// <summary>
/// The <c>platform-http</c> component (Camel's <c>platform-http</c>): consumers served by the application's ASP.NET Core server. Each started
/// consumer is an ASP.NET Core endpoint of the <see cref="PlatformHttpEngine"/>'s data source (mapped with <c>app.MapCaravan()</c>), so
/// starting a route adds its endpoint and stopping it removes it. It also serves the REST DSL (<see cref="IRestConsumerFactory"/>).
/// <para>URI: <c>platform-http:/path[?options]</c>, e.g. <c>platform-http:/orders/{id}?httpMethodRestrict=GET,PUT</c>.</para>
/// </summary>
public sealed class PlatformHttpComponent : ComponentBase, IRestConsumerFactory
{
    /// <summary>The component's scheme.</summary>
    public const string Scheme = "platform-http";

    /// <summary>Creates the component with an engine of its own (map it with <c>endpoints.MapPlatformHttp(component.Engine)</c>).</summary>
    public PlatformHttpComponent()
        : this(new PlatformHttpEngine())
    {
    }

    /// <summary>Creates the component on <paramref name="engine"/>.</summary>
    /// <param name="engine">The engine (shared with the application's endpoint routing).</param>
    public PlatformHttpComponent(PlatformHttpEngine engine)
    {
        ArgumentNullException.ThrowIfNull(engine);
        Engine = engine;
    }

    /// <summary>The engine serving the consumers.</summary>
    public PlatformHttpEngine Engine { get; }

    /// <summary>Default of the endpoints' <c>muteException</c> option (default false, as in Camel: a failure's text is the 500 response).</summary>
    public bool MuteException { get; set; }

    /// <summary>The status codes of failures by exception type (the closest base type of the exception wins); others are 500.</summary>
    public Dictionary<Type, int> ExceptionStatusCodes { get; } = [];

    /// <summary>The default header filter of the endpoints.</summary>
    public Spi.IHeaderFilterStrategy HeaderFilterStrategy { get; set; } = new HttpHeaderFilterStrategy();

    /// <summary>Answers failures with <typeparamref name="TException"/> (or a subtype) with <paramref name="statusCode"/>.</summary>
    /// <typeparam name="TException">The exception type.</typeparam>
    /// <param name="statusCode">The status code.</param>
    /// <returns>This component.</returns>
    public PlatformHttpComponent MapException<TException>(int statusCode)
        where TException : Exception
    {
        ExceptionStatusCodes[typeof(TException)] = statusCode;
        return this;
    }

    /// <inheritdoc />
    public IConsumer CreateConsumer(
        ICaravanContext context,
        IProcessor processor,
        string method,
        string basePath,
        string? uriTemplate,
        string? consumes,
        string? produces,
        RestConfiguration configuration,
        IReadOnlyDictionary<string, string> parameters)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(configuration);
        var path = CombinePaths(configuration.ContextPath, basePath, uriTemplate);
        var options = new Dictionary<string, string>(configuration.EndpointProperties, StringComparer.OrdinalIgnoreCase);
        var enableCors = configuration.EnableCors;
        foreach (var (key, value) in parameters)
        {
            if (string.Equals(key, "enableCors", StringComparison.OrdinalIgnoreCase))
            {
                enableCors = bool.Parse(value);
            }
            else
            {
                options[key] = value;
            }
        }

        options["httpMethodRestrict"] = method;
        var uri = new StringBuilder(Scheme).Append(':').Append(path).Append('?')
            .AppendJoin('&', options.OrderBy(o => o.Key, StringComparer.OrdinalIgnoreCase).Select(o => o.Key + "=" + Uri.EscapeDataString(o.Value)));
        if (enableCors)
        {
            uri.Append("&enableCors=true");
        }

        var endpoint = context.GetEndpoint<PlatformHttpEndpoint>(uri.ToString());
        endpoint.RestProduces = produces;
        endpoint.CorsHeaders = enableCors ? configuration.GetCorsHeaders() : null;
        return endpoint.CreateConsumer(processor);
    }

    /// <summary>Joins path segments with single slashes; the result starts with a slash.</summary>
    /// <param name="parts">The segments (null and empty ones are skipped).</param>
    /// <returns>The path.</returns>
    public static string CombinePaths(params string?[] parts)
    {
        var segments = parts.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p!.Trim().Trim('/')).Where(p => p.Length > 0);
        return "/" + string.Join('/', segments);
    }

    /// <inheritdoc />
    protected override EndpointBase CreateEndpoint(string uri, string remaining, IDictionary<string, string> parameters)
    {
        var path = CombinePaths(remaining);
        return new PlatformHttpEndpoint(uri, this, path)
        {
            MuteException = MuteException,
            HeaderFilterStrategy = HeaderFilterStrategy,
        };
    }

    /// <summary>The status code of a failure.</summary>
    /// <param name="exception">The exception.</param>
    /// <returns>The mapped code, or 500.</returns>
    internal int StatusCodeOf(Exception exception)
    {
        for (var type = exception.GetType(); type is not null; type = type.BaseType)
        {
            if (ExceptionStatusCodes.TryGetValue(type, out var code))
            {
                return code;
            }
        }

        return 500;
    }

    /// <inheritdoc />
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"PlatformHttpComponent[{ExceptionStatusCodes.Count} mappings]");
}
