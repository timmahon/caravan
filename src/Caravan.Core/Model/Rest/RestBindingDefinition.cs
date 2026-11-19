using Caravan.DataFormat;
using Caravan.Processors;
using Caravan.Spi;
using Caravan.Support;

namespace Caravan.Model.Rest;

/// <summary>
/// The binding of a REST service (Camel's <c>RestBindingDefinition</c>, run as <c>RestBindingAdvice</c>): the single top-level node of a route
/// created from a <see cref="VerbDefinition"/>, holding the service's steps. See <see cref="RestBindingProcessor"/>. Options left null inherit
/// the context's <see cref="RestConfiguration"/> when the route is created.
/// </summary>
public sealed class RestBindingDefinition : OutputDefinition
{
    /// <summary>The HTTP method of the service in upper case.</summary>
    public string Method { get; set; } = "GET";

    /// <summary>The media types accepted.</summary>
    public string? Consumes { get; set; }

    /// <summary>The media types produced.</summary>
    public string? Produces { get; set; }

    /// <summary>The binding mode; null uses the configuration's.</summary>
    public RestBindingMode? BindingMode { get; set; }

    /// <summary>The type the request body is bound to.</summary>
    public Type? Type { get; set; }

    /// <summary>The type of the response body.</summary>
    public Type? OutType { get; set; }

    /// <summary>Whether responses with a code of 300 or more are not bound; null uses the configuration's.</summary>
    public bool? SkipBindingOnErrorCode { get; set; }

    /// <summary>Whether requests are validated; null uses the configuration's.</summary>
    public bool? ClientRequestValidation { get; set; }

    /// <summary>Whether CORS headers are added; null uses the configuration's.</summary>
    public bool? EnableCors { get; set; }

    /// <summary>The service's parameters (defaults and validation).</summary>
    public List<RestParamDefinition> Params { get; } = [];

    /// <inheritdoc />
    public override string ShortName => "restBinding";

    /// <inheritdoc />
    public override IProcessor CreateProcessor(RouteContext routeContext)
    {
        var context = routeContext.Context;
        var configuration = context.GetRestConfiguration();
        var mode = BindingMode ?? configuration.BindingMode;
        IDataFormat? jsonIn = null, jsonOut = null, xmlIn = null, xmlOut = null;
        if (mode is RestBindingMode.Auto or RestBindingMode.Json or RestBindingMode.JsonXml)
        {
            var name = configuration.JsonDataFormat ?? "json";
            jsonIn = CreateDataFormat(context, configuration, name, Type, "json.in.");
            jsonOut = CreateDataFormat(context, configuration, name, OutType, "json.out.");
        }

        if (mode is RestBindingMode.Auto or RestBindingMode.Xml or RestBindingMode.JsonXml)
        {
            var name = configuration.XmlDataFormat ?? "xml";
            xmlIn = Type is null ? null : CreateDataFormat(context, configuration, name, Type, "xml.in.");
            xmlOut = CreateDataFormat(context, configuration, name, OutType, "xml.out.");
        }

        var settings = new RestBindingSettings
        {
            Method = Method,
            Consumes = Consumes,
            Produces = Produces,
            BindingMode = mode,
            SkipBindingOnErrorCode = SkipBindingOnErrorCode ?? configuration.SkipBindingOnErrorCode,
            ClientRequestValidation = ClientRequestValidation ?? configuration.ClientRequestValidation,
            CorsHeaders = (EnableCors ?? configuration.EnableCors) ? configuration.GetCorsHeaders() : null,
            Params = [.. Params],
            JsonUnmarshal = jsonIn,
            JsonMarshal = jsonOut,
            XmlUnmarshal = xmlIn,
            XmlMarshal = xmlOut,
        };
        return new RestBindingProcessor(settings, routeContext.CreateBlockProcessor(Outputs, this));
    }

    /// <inheritdoc />
    public override string Label => $"restBinding[{Method}]";

    private static IDataFormat CreateDataFormat(ICaravanContext context, RestConfiguration configuration, string name, Type? type, string prefix)
    {
        // Typed binding needs an instance of its own: copy the built-in formats rather than reconfigure a shared one.
        var dataFormat = context.ResolveDataFormat(name) switch
        {
            JsonDataFormat json => new JsonDataFormat(type ?? json.UnmarshalType)
            {
                SerializerOptions = json.SerializerOptions,
                PrettyPrint = json.PrettyPrint,
                Include = json.Include,
                UseList = json.UseList,
                ContentTypeHeader = json.ContentTypeHeader,
            },
            XmlDataFormat xml => new XmlDataFormat(type ?? xml.UnmarshalType)
            {
                PrettyPrint = xml.PrettyPrint,
                Fragment = xml.Fragment,
                Encoding = xml.Encoding,
                ContentTypeHeader = xml.ContentTypeHeader,
            },
            var other => other,
        };
        if (type is not null && dataFormat is not JsonDataFormat and not XmlDataFormat)
        {
            PropertyBinder.TrySetProperty(context, dataFormat, "unmarshalType", type);
        }

        var kind = prefix[..prefix.IndexOf('.')];
        var properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in configuration.DataFormatProperties)
        {
            if (key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                properties[key[prefix.Length..]] = value;
            }
            else if (!key.StartsWith("json.", StringComparison.OrdinalIgnoreCase) && !key.StartsWith("xml.", StringComparison.OrdinalIgnoreCase))
            {
                properties.TryAdd(key, value);
            }
            else if (key.StartsWith(kind + ".", StringComparison.OrdinalIgnoreCase) && !key.Contains(".in.", StringComparison.OrdinalIgnoreCase)
                && !key.Contains(".out.", StringComparison.OrdinalIgnoreCase))
            {
                properties.TryAdd(key[(kind.Length + 1)..], value);
            }
        }

        if (properties.Count > 0)
        {
            var unknown = PropertyBinder.Bind(context, dataFormat, properties);
            if (unknown.Count > 0)
            {
                throw new ArgumentException($"Unknown properties of the {name} data format in the REST configuration: {string.Join(", ", unknown)}");
            }
        }

        return dataFormat;
    }
}
