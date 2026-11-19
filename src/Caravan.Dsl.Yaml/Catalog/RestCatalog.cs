using Caravan.Model.Rest;
using YamlDotNet.RepresentationModel;

namespace Caravan.Dsl.Yaml.Catalog;

/// <summary>The REST DSL elements of the YAML DSL: <c>rest</c> (with its verbs) and <c>restConfiguration</c>, as Camel writes them.</summary>
internal static class RestCatalog
{
    private static readonly string[] BindingModes = ["off", "auto", "json", "xml", "json_xml"];

    /// <summary>A <c>key</c>/<c>value</c> pair (Camel's <c>RestPropertyDefinition</c>).</summary>
    public static readonly ObjectSpec Property = new()
    {
        Options = [Opt.Str("key", "The key.", required: true), Opt.Str("value", "The value.", required: true)],
    };

    /// <summary>A security requirement.</summary>
    public static readonly ObjectSpec SecurityRequirement = new()
    {
        Options =
        [
            Opt.Str("key", "The key of a security definition.", required: true),
            Opt.Str("scopes", "The scopes required, comma-separated."),
        ],
    };

    /// <summary>A security scheme (<c>basicAuth</c>, <c>bearerToken</c>, <c>apiKey</c>).</summary>
    public static readonly ObjectSpec SecurityScheme = new()
    {
        Options =
        [
            Opt.Str("key", "The key services refer to.", required: true),
            Opt.Str("description", "A description."),
            Opt.Str("format", "bearerToken: the token format, e.g. JWT."),
            Opt.Str("name", "apiKey: the name of the header, query parameter or cookie."),
            Opt.Bool("inHeader", "apiKey: the key is a header."),
            Opt.Bool("inQuery", "apiKey: the key is a query parameter."),
            Opt.Bool("inCookie", "apiKey: the key is a cookie."),
        ],
    };

    /// <summary>The <c>securityDefinitions</c> of a <c>rest</c>.</summary>
    public static readonly ObjectSpec SecurityDefinitions = new()
    {
        Options =
        [
            Opt.Objects("basicAuth", "HTTP basic authentication schemes.", SecurityScheme),
            Opt.Objects("bearerToken", "HTTP bearer token schemes.", SecurityScheme),
            Opt.Objects("apiKey", "API key schemes.", SecurityScheme),
            Opt.Unsupported("oauth2", "only basicAuth, bearerToken and apiKey security definitions are supported"),
            Opt.Unsupported("openIdConnect", "only basicAuth, bearerToken and apiKey security definitions are supported"),
            Opt.Unsupported("mutualTLS", "only basicAuth, bearerToken and apiKey security definitions are supported"),
        ],
    };

    /// <summary>A parameter of a verb.</summary>
    public static readonly ObjectSpec Param = new()
    {
        Options =
        [
            Opt.Str("name", "The parameter name.", required: true),
            Opt.Enum("type", "Where the parameter is (default path).", "path", "query", "header", "body", "formData"),
            Opt.Bool("required", "Whether it is required (default true)."),
            Opt.Str("defaultValue", "A default value (set as a header when a query parameter is missing)."),
            Opt.Str("dataType", "The OpenAPI data type (default string)."),
            Opt.Str("dataFormat", "The OpenAPI format, e.g. int64."),
            Opt.Str("arrayType", "The element type of an array parameter."),
            Opt.Any("allowableValues", "The values allowed: a list of strings or of { value: ... }."),
            Opt.Str("description", "A description."),
            Opt.Ignored("collectionFormat", OptionKind.String, "OpenAPI 2 only"),
            Opt.Ignored("examples", OptionKind.Any, "examples are not documented"),
        ],
    };

    /// <summary>A header of a documented response.</summary>
    public static readonly ObjectSpec ResponseHeader = new()
    {
        Options =
        [
            Opt.Str("name", "The header name.", required: true),
            Opt.Str("description", "A description."),
            Opt.Str("dataType", "The OpenAPI data type (default string)."),
            Opt.Str("dataFormat", "The OpenAPI format."),
            Opt.Any("allowableValues", "The values allowed."),
            Opt.Ignored("arrayType", OptionKind.String, "array headers are documented as strings"),
            Opt.Ignored("collectionFormat", OptionKind.String, "OpenAPI 2 only"),
            Opt.Ignored("example", OptionKind.String, "examples are not documented"),
        ],
    };

    /// <summary>A documented response of a verb.</summary>
    public static readonly ObjectSpec ResponseMessage = new()
    {
        Options =
        [
            Opt.Str("code", "The status code, or default (default 200)."),
            Opt.Str("message", "The description.", required: true),
            Opt.Type("responseModel", "The type of the body."),
            Opt.Objects("header", "The response headers.", ResponseHeader),
            Opt.Ignored("examples", OptionKind.Any, "examples are not documented"),
        ],
    };

    /// <summary>The <c>to</c> of a verb.</summary>
    public static readonly ObjectSpec To = new()
    {
        InlineOption = "uri",
        Options =
        [
            Opt.Str("uri", "The endpoint URI.", required: true),
            Opt.Map("parameters", "Endpoint options appended to the URI as query parameters."),
        ],
    };

    /// <summary>A service (<c>get</c>, <c>post</c>, ...).</summary>
    public static readonly ObjectSpec Verb = new()
    {
        Options =
        [
            Opt.Str("id", "The id of the service (also the route id unless routeId is set)."),
            Opt.Str("routeId", "The id of the route created for the service."),
            Opt.Str("description", "A description (the OpenAPI summary)."),
            Opt.Str("path", "The path relative to the rest's path, with {name} parameters.").Alias("uri"),
            Opt.Str("consumes", "The media types accepted (comma-separated)."),
            Opt.Str("produces", "The media types produced (comma-separated)."),
            Opt.Type("type", "The type the request body is bound to (Type[] for a list)."),
            Opt.Type("outType", "The type of the response body."),
            Opt.Enum("bindingMode", "The binding mode.", BindingModes),
            Opt.Bool("skipBindingOnErrorCode", "Whether responses with a code of 300 or more are not bound."),
            Opt.Bool("clientRequestValidation", "Whether requests are validated (415, 406, 400)."),
            Opt.Bool("enableCORS", "Whether CORS headers are added.").Alias("enableCors"),
            Opt.Bool("apiDocs", "Whether the service is in the OpenAPI document (default true)."),
            Opt.Bool("deprecated", "Whether the service is deprecated."),
            Opt.Bool("disabled", "Whether the service is disabled (no route is created)."),
            Opt.Object("to", "The endpoint requests are sent to.", To),
            Opt.Steps("steps", "Caravan: the steps of an inline route (instead of to)."),
            Opt.Objects("param", "The parameters.", Param),
            Opt.Objects("responseMessage", "The documented responses.", ResponseMessage),
            Opt.Objects("security", "The security requirements.", SecurityRequirement),
            Opt.Ignored("enableNoContentResponse", OptionKind.Boolean, "an empty body is always answered with 204"),
            Opt.Ignored("streamCache", OptionKind.Boolean, "stream caching is a context and route option"),
            Opt.Unsupported("inputType", "data type contracts are not supported"),
            Opt.Unsupported("outputType", "data type contracts are not supported"),
        ],
    };

    /// <summary>A <c>rest</c>.</summary>
    public static readonly ObjectSpec Rest = new()
    {
        Options =
        [
            Opt.Str("id", "The id."),
            Opt.Str("description", "A description."),
            Opt.Str("path", "The base path of the services."),
            Opt.Str("tag", "The OpenAPI tag."),
            Opt.Str("consumes", "The media types accepted by default."),
            Opt.Str("produces", "The media types produced by default."),
            Opt.Enum("bindingMode", "The binding mode of the services.", BindingModes),
            Opt.Bool("skipBindingOnErrorCode", "Whether responses with a code of 300 or more are not bound."),
            Opt.Bool("clientRequestValidation", "Whether requests are validated."),
            Opt.Bool("enableCORS", "Whether CORS headers are added.").Alias("enableCors"),
            Opt.Bool("apiDocs", "Whether the services are in the OpenAPI document (default true)."),
            Opt.Bool("disabled", "Whether the whole rest is disabled."),
            Opt.Object("securityDefinitions", "The security schemes.", SecurityDefinitions),
            Opt.Objects("securityRequirements", "The security requirements of every service.", SecurityRequirement) with { ItemWrapper = "securityRequirement" },
            Opt.Objects("get", "GET services.", Verb),
            Opt.Objects("post", "POST services.", Verb),
            Opt.Objects("put", "PUT services.", Verb),
            Opt.Objects("patch", "PATCH services.", Verb),
            Opt.Objects("delete", "DELETE services.", Verb),
            Opt.Objects("head", "HEAD services.", Verb),
            Opt.Objects("options", "Caravan: OPTIONS services.", Verb),
            Opt.Ignored("enableNoContentResponse", OptionKind.Boolean, "an empty body is always answered with 204"),
            Opt.Ignored("streamCache", OptionKind.Boolean, "stream caching is a context and route option"),
        ],
    };

    /// <summary>A <c>restConfiguration</c>.</summary>
    public static readonly ObjectSpec Configuration = new()
    {
        Options =
        [
            Opt.Str("component", "The component serving REST services (default platform-http)."),
            Opt.Str("scheme", "The scheme (OpenAPI servers)."),
            Opt.Str("host", "The host (OpenAPI servers)."),
            Opt.Int("port", "The port (OpenAPI servers)."),
            Opt.Str("contextPath", "A path prepended to every service."),
            Opt.Str("apiContextPath", "The path of the OpenAPI document (under contextPath)."),
            Opt.Enum("bindingMode", "The binding mode (default off).", BindingModes),
            Opt.Bool("skipBindingOnErrorCode", "Whether responses with a code of 300 or more are not bound (default true)."),
            Opt.Bool("clientRequestValidation", "Whether requests are validated (default false)."),
            Opt.Bool("enableCORS", "Whether CORS headers are added (default false).").Alias("enableCors"),
            Opt.Bool("inlineRoutes", "Whether direct routes are merged into their service (default true)."),
            Opt.Str("jsonDataFormat", "The data format binding JSON (default json)."),
            Opt.Str("xmlDataFormat", "The data format binding XML (default xml)."),
            Opt.Objects("dataFormatProperty", "Properties of the binding data formats (json.in., json.out., xml.in., xml.out. prefixes).", Property),
            Opt.Objects("endpointProperty", "Options of the consumers serving the services.", Property),
            Opt.Objects("apiProperty", "OpenAPI properties (api.title, api.version, api.description...).", Property),
            Opt.Objects("corsHeaders", "CORS headers overriding the defaults.", Property),
            Opt.Ignored("apiComponent", OptionKind.String, "the OpenAPI document is served by Caravan.AspNetCore"),
            Opt.Ignored("producerComponent", OptionKind.String, "there is no REST producer"),
            Opt.Ignored("producerApiDoc", OptionKind.String, "there is no REST producer"),
            Opt.Ignored("apiHost", OptionKind.String, "use host"),
            Opt.Ignored("apiContextRouteId", OptionKind.String, "the OpenAPI document is not a route"),
            Opt.Ignored("apiVendorExtension", OptionKind.Boolean, "no vendor extensions are written"),
            Opt.Ignored("useXForwardHeaders", OptionKind.Boolean, "use ASP.NET Core's forwarded headers middleware"),
            Opt.Ignored("hostNameResolver", OptionKind.String, "the host decides"),
            Opt.Ignored("bindingPackageScan", OptionKind.String, "types are given by name"),
            Opt.Ignored("enableNoContentResponse", OptionKind.Boolean, "an empty body is always answered with 204"),
            Opt.Ignored("componentProperty", OptionKind.Any, "configure the component in code"),
            Opt.Ignored("consumerProperty", OptionKind.Any, "use endpointProperty"),
        ],
    };

    /// <summary>The verbs of <see cref="Rest"/>.</summary>
    public static readonly string[] Verbs = ["get", "post", "put", "patch", "delete", "head", "options"];

    /// <summary>Applies a <c>restConfiguration</c> to <paramref name="configuration"/>.</summary>
    public static void FillConfiguration(YamlObject options, RestConfiguration configuration)
    {
        configuration.Component = options.String("component") ?? configuration.Component;
        configuration.Scheme = options.String("scheme") ?? configuration.Scheme;
        configuration.Host = options.String("host") ?? configuration.Host;
        configuration.Port = options.Int("port") ?? configuration.Port;
        configuration.ContextPath = options.String("contextPath") ?? configuration.ContextPath;
        configuration.ApiContextPath = options.String("apiContextPath") ?? configuration.ApiContextPath;
        configuration.BindingMode = options.Enum<RestBindingMode>("bindingMode") ?? configuration.BindingMode;
        configuration.SkipBindingOnErrorCode = options.Bool("skipBindingOnErrorCode") ?? configuration.SkipBindingOnErrorCode;
        configuration.ClientRequestValidation = options.Bool("clientRequestValidation") ?? configuration.ClientRequestValidation;
        configuration.EnableCors = options.Bool("enableCORS") ?? configuration.EnableCors;
        configuration.InlineRoutes = options.Bool("inlineRoutes") ?? configuration.InlineRoutes;
        configuration.JsonDataFormat = options.String("jsonDataFormat") ?? configuration.JsonDataFormat;
        configuration.XmlDataFormat = options.String("xmlDataFormat") ?? configuration.XmlDataFormat;
        Properties(options, "dataFormatProperty", configuration.DataFormatProperties);
        Properties(options, "endpointProperty", configuration.EndpointProperties);
        Properties(options, "apiProperty", configuration.ApiProperties);
        Properties(options, "corsHeaders", configuration.CorsHeaders);
    }

    /// <summary>Fills a REST definition from a <c>rest</c>.</summary>
    public static void FillRest(YamlObject options, RestDefinition rest)
    {
        rest.Id = options.String("id");
        rest.Description = options.String("description");
        rest.Path = options.String("path");
        rest.Tag = options.String("tag");
        rest.Consumes = options.String("consumes");
        rest.Produces = options.String("produces");
        rest.BindingMode = options.Enum<RestBindingMode>("bindingMode");
        rest.SkipBindingOnErrorCode = options.Bool("skipBindingOnErrorCode");
        rest.ClientRequestValidation = options.Bool("clientRequestValidation");
        rest.EnableCors = options.Bool("enableCORS");
        rest.ApiDocs = options.Bool("apiDocs") ?? true;
        rest.Disabled = options.Bool("disabled") ?? false;
        if (options.Object("securityDefinitions") is { } security)
        {
            Schemes(security, "basicAuth", RestSecurityType.Basic, rest);
            Schemes(security, "bearerToken", RestSecurityType.Bearer, rest);
            Schemes(security, "apiKey", RestSecurityType.ApiKey, rest);
        }

        rest.SecurityRequirements.AddRange(options.Objects("securityRequirements").Select(Requirement));
        foreach (var method in Verbs)
        {
            foreach (var verb in options.Objects(method))
            {
                FillVerb(verb, rest.AddVerb(method, verb.String("path")));
            }
        }
    }

    private static void FillVerb(YamlObject options, VerbDefinition verb)
    {
        verb.Id = options.String("id");
        verb.RouteId = options.String("routeId");
        verb.Description = options.String("description");
        verb.Consumes = options.String("consumes");
        verb.Produces = options.String("produces");
        verb.Type = options.Type("type");
        verb.OutType = options.Type("outType");
        verb.BindingMode = options.Enum<RestBindingMode>("bindingMode");
        verb.SkipBindingOnErrorCode = options.Bool("skipBindingOnErrorCode");
        verb.ClientRequestValidation = options.Bool("clientRequestValidation");
        verb.EnableCors = options.Bool("enableCORS");
        verb.ApiDocs = options.Bool("apiDocs");
        verb.Deprecated = options.Bool("deprecated") ?? false;
        verb.Disabled = options.Bool("disabled") ?? false;
        if (options.Object("to") is { } to)
        {
            verb.To = EndpointUris.Build(to);
        }

        options.Steps("steps", verb.Outputs);
        if (verb.To is null == (verb.Outputs.Count == 0))
        {
            throw options.Context.Error(options.Node, $"The REST service '{verb.Method} {verb.Path}' needs either to or steps");
        }

        foreach (var param in options.Objects("param"))
        {
            var definition = new RestParamDefinition
            {
                Name = param.RequiredString("name"),
                Type = param.Enum<RestParamType>("type") ?? RestParamType.Path,
                Required = param.Bool("required") ?? true,
                DefaultValue = param.String("defaultValue"),
                DataType = param.String("dataType"),
                DataFormat = param.String("dataFormat"),
                ArrayType = param.String("arrayType"),
                Description = param.String("description"),
            };
            definition.AllowableValues.AddRange(Values(param, "allowableValues"));
            verb.Params.Add(definition);
        }

        foreach (var response in options.Objects("responseMessage"))
        {
            var definition = new ResponseMessageDefinition
            {
                Code = response.String("code") ?? "200",
                Message = response.String("message"),
                ResponseModel = response.Type("responseModel"),
            };
            foreach (var header in response.Objects("header"))
            {
                var headerDefinition = new ResponseHeaderDefinition
                {
                    Name = header.RequiredString("name"),
                    Description = header.String("description"),
                    DataType = header.String("dataType"),
                    DataFormat = header.String("dataFormat"),
                };
                headerDefinition.AllowableValues.AddRange(Values(header, "allowableValues"));
                definition.Headers.Add(headerDefinition);
            }

            verb.ResponseMessages.Add(definition);
        }

        verb.Security.AddRange(options.Objects("security").Select(Requirement));
    }

    private static void Schemes(YamlObject security, string name, RestSecurityType type, RestDefinition rest)
    {
        foreach (var scheme in security.Objects(name))
        {
            rest.SecurityDefinitions.Add(new RestSecurityDefinition
            {
                Key = scheme.RequiredString("key"),
                Type = type,
                Description = scheme.String("description"),
                Format = scheme.String("format"),
                Name = scheme.String("name"),
                In = scheme.Bool("inQuery") == true ? "query" : scheme.Bool("inCookie") == true ? "cookie" : type == RestSecurityType.ApiKey ? "header" : null,
            });
        }
    }

    private static RestSecurityRequirement Requirement(YamlObject options)
    {
        var requirement = new RestSecurityRequirement { Key = options.RequiredString("key") };
        if (options.String("scopes") is { } scopes)
        {
            requirement.Scopes.AddRange(scopes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }

        return requirement;
    }

    private static void Properties(YamlObject options, string name, Dictionary<string, string> target)
    {
        foreach (var property in options.Objects(name))
        {
            target[property.RequiredString("key")] = property.RequiredString("value");
        }
    }

    private static IEnumerable<string> Values(YamlObject options, string name)
    {
        var node = options.Raw(name);
        IEnumerable<YamlNode> items = node switch
        {
            null => [],
            YamlSequenceNode sequence => sequence.Children,
            var single => [single],
        };
        foreach (var item in items)
        {
            yield return item switch
            {
                YamlScalarNode scalar => options.Context.Text(scalar.Value ?? string.Empty),
                YamlMappingNode mapping when mapping.Children.Count == 1 && mapping.Children.First() is { Key: YamlScalarNode { Value: "value" }, Value: YamlScalarNode value } =>
                    options.Context.Text(value.Value ?? string.Empty),
                _ => throw options.Context.Error(item, $"Option '{name}' must be a list of values or of {{ value: ... }}"),
            };
        }
    }
}
