using System.Collections;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using System.Text.Json.Serialization.Metadata;
using Caravan.Model.Rest;

namespace Caravan.AspNetCore.OpenApi;

/// <summary>
/// Generates an OpenAPI 3.1 document from the REST services of a context (Camel's <c>camel-openapi-java</c>): paths, operations, parameters,
/// request bodies, responses, tags and security schemes; schemas of the body types come from System.Text.Json's
/// <see cref="JsonSchemaExporter"/> with the JSON binding's naming (camelCase), one component per named type.
/// </summary>
public static class OpenApiGenerator
{
    /// <summary>The OpenAPI version written.</summary>
    public const string OpenApiVersion = "3.1.0";

    private static readonly JsonSerializerOptions SchemaOptions = new(JsonSerializerDefaults.Web)
    {
        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.Strict,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
    };

    private static readonly JsonSchemaExporterOptions ExporterOptions = new() { TreatNullObliviousAsNonNullable = true };

    /// <summary>Generates the document.</summary>
    /// <param name="context">The context whose REST services are documented (those whose route exists).</param>
    /// <returns>The document.</returns>
    public static JsonObject Generate(ICaravanContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var configuration = context.GetRestConfiguration();
        var schemas = new SortedDictionary<string, JsonNode>(StringComparer.Ordinal);
        var tags = new List<(string Name, string? Description)>();
        var paths = new SortedDictionary<string, JsonObject>(StringComparer.Ordinal);
        var securitySchemes = new SortedDictionary<string, JsonNode>(StringComparer.Ordinal);
        foreach (var (route, verb) in context.GetRestServices())
        {
            var rest = verb.Rest ?? new RestDefinition();
            if (!rest.ApiDocs || verb.ApiDocs == false)
            {
                continue;
            }

            var basePath = PlatformHttpComponent.CombinePaths(rest.Path);
            var tag = rest.Tag ?? (basePath == "/" ? null : basePath.TrimStart('/'));
            if (tag is not null && !tags.Exists(t => t.Name == tag))
            {
                tags.Add((tag, rest.Description));
            }

            foreach (var security in rest.SecurityDefinitions)
            {
                securitySchemes[security.Key] = SecurityScheme(security);
            }

            var path = PlatformHttpComponent.CombinePaths(rest.Path, verb.Path);
            if (!paths.TryGetValue(path, out var item))
            {
                paths[path] = item = [];
            }

            item[verb.Method.ToLowerInvariant()] = Operation(route.Id, rest, verb, tag, path, schemas);
        }

        var document = new JsonObject
        {
            ["openapi"] = OpenApiVersion,
            ["info"] = Info(configuration),
            ["servers"] = new JsonArray(new JsonObject { ["url"] = ServerUrl(configuration) }),
        };
        if (tags.Count > 0)
        {
            document["tags"] = new JsonArray([.. tags.Select(t => Object(("name", t.Name), ("description", t.Description)))]);
        }

        var pathsNode = new JsonObject();
        foreach (var (path, item) in paths)
        {
            pathsNode[path] = item;
        }

        document["paths"] = pathsNode;
        var components = new JsonObject();
        if (schemas.Count > 0)
        {
            components["schemas"] = ToObject(schemas);
        }

        if (securitySchemes.Count > 0)
        {
            components["securitySchemes"] = ToObject(securitySchemes);
        }

        if (components.Count > 0)
        {
            document["components"] = components;
        }

        return document;
    }

    /// <summary>Generates the document as indented JSON.</summary>
    /// <param name="context">The context.</param>
    /// <returns>The JSON text.</returns>
    public static string GenerateJson(ICaravanContext context) =>
        Generate(context).ToJsonString(new JsonSerializerOptions { WriteIndented = true });

    /// <summary>The JSON schema of <paramref name="type"/>: a reference to a component for named object types, else inline.</summary>
    /// <param name="type">The type.</param>
    /// <param name="schemas">The components, by name.</param>
    /// <returns>The schema.</returns>
    public static JsonNode Schema(Type type, IDictionary<string, JsonNode> schemas)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(schemas);
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (ElementType(type) is { } element)
        {
            return new JsonObject { ["type"] = "array", ["items"] = Schema(element, schemas) };
        }

        var schema = JsonSchemaExporter.GetJsonSchemaAsNode(SchemaOptions, type, ExporterOptions);
        if (!IsNamedObject(type, schema))
        {
            return schema;
        }

        var name = SchemaName(type);
        if (!schemas.ContainsKey(name))
        {
            schemas[name] = schema;
        }

        return new JsonObject { ["$ref"] = "#/components/schemas/" + name };
    }

    private static JsonObject Operation(string? routeId, RestDefinition rest, VerbDefinition verb, string? tag, string path, IDictionary<string, JsonNode> schemas)
    {
        var operation = new JsonObject();
        if (tag is not null)
        {
            operation["tags"] = new JsonArray(tag);
        }

        if (verb.Description is not null)
        {
            operation["summary"] = verb.Description;
        }

        if ((verb.Id ?? routeId) is { } id)
        {
            operation["operationId"] = id;
        }

        if (verb.Deprecated)
        {
            operation["deprecated"] = true;
        }

        var consumes = MediaTypes(verb.Consumes ?? rest.Consumes);
        var produces = MediaTypes(verb.Produces ?? rest.Produces);
        var parameters = new JsonArray();
        foreach (var param in verb.Params.Where(p => p.Type is RestParamType.Path or RestParamType.Query or RestParamType.Header))
        {
            parameters.Add(Parameter(param));
        }

        foreach (var name in PathParameters(path).Where(n => !verb.Params.Exists(p => p.Type == RestParamType.Path && p.Name == n)))
        {
            parameters.Add(new JsonObject { ["name"] = name, ["in"] = "path", ["required"] = true, ["schema"] = new JsonObject { ["type"] = "string" } });
        }

        if (parameters.Count > 0)
        {
            operation["parameters"] = parameters;
        }

        if (RequestBody(verb, consumes, schemas) is { } body)
        {
            operation["requestBody"] = body;
        }

        operation["responses"] = Responses(verb, produces, schemas);
        var security = rest.SecurityRequirements.Concat(verb.Security).ToList();
        if (security.Count > 0)
        {
            operation["security"] = new JsonArray([.. security.Select(s => new JsonObject { [s.Key] = new JsonArray([.. s.Scopes.Select(x => (JsonNode)x)]) })]);
        }

        return operation;
    }

    private static JsonObject Parameter(RestParamDefinition param)
    {
        var parameter = Object(
            ("name", param.Name),
            ("in", param.Type switch { RestParamType.Query => "query", RestParamType.Header => "header", _ => "path" }),
            ("description", param.Description));
        parameter["required"] = param.Type == RestParamType.Path || param.Required;
        parameter["schema"] = ParamSchema(param.DataType, param.DataFormat, param.ArrayType, param.AllowableValues, param.DefaultValue);
        return parameter;
    }

    private static JsonObject ParamSchema(string? dataType, string? dataFormat, string? arrayType, IReadOnlyList<string> allowable, string? defaultValue)
    {
        var type = dataType ?? "string";
        var schema = new JsonObject { ["type"] = type };
        if (dataFormat is not null)
        {
            schema["format"] = dataFormat;
        }

        if (type == "array")
        {
            schema["items"] = new JsonObject { ["type"] = arrayType ?? "string" };
        }

        if (allowable.Count > 0)
        {
            schema["enum"] = new JsonArray([.. allowable.Select(v => TypedValue(type, v))]);
        }

        if (defaultValue is not null)
        {
            schema["default"] = TypedValue(type, defaultValue);
        }

        return schema;
    }

    private static JsonNode? TypedValue(string type, string value) => type switch
    {
        "integer" when long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l) => l,
        "number" when double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) => d,
        "boolean" when bool.TryParse(value, out var b) => b,
        _ => value,
    };

    private static JsonObject? RequestBody(VerbDefinition verb, IReadOnlyList<string> consumes, IDictionary<string, JsonNode> schemas)
    {
        var bodyParam = verb.Params.Find(p => p.Type == RestParamType.Body);
        var form = verb.Params.Where(p => p.Type == RestParamType.FormData).ToList();
        var content = new JsonObject();
        if (bodyParam is not null || verb.Type is not null)
        {
            foreach (var mediaType in consumes.Count > 0 ? consumes : ["application/json"])
            {
                content[mediaType] = new JsonObject
                {
                    ["schema"] = verb.Type is { } type ? Schema(type, schemas) : ParamSchema(bodyParam!.DataType, bodyParam.DataFormat, bodyParam.ArrayType, [], null),
                };
            }
        }

        if (form.Count > 0)
        {
            var properties = new JsonObject();
            foreach (var param in form)
            {
                var schema = ParamSchema(param.DataType, param.DataFormat, param.ArrayType, param.AllowableValues, param.DefaultValue);
                if (param.Description is not null)
                {
                    schema["description"] = param.Description;
                }

                properties[param.Name] = schema;
            }

            var formSchema = new JsonObject { ["type"] = "object", ["properties"] = properties };
            var required = form.Where(p => p.Required).Select(p => (JsonNode)p.Name).ToArray();
            if (required.Length > 0)
            {
                formSchema["required"] = new JsonArray(required);
            }

            content["application/x-www-form-urlencoded"] = new JsonObject { ["schema"] = formSchema };
        }

        if (content.Count == 0)
        {
            return null;
        }

        var body = Object(("description", bodyParam?.Description));
        body["required"] = bodyParam?.Required ?? verb.Type is not null;
        body["content"] = content;
        return body;
    }

    private static JsonObject Responses(VerbDefinition verb, IReadOnlyList<string> produces, IDictionary<string, JsonNode> schemas)
    {
        var responses = new JsonObject();
        var messages = verb.ResponseMessages.Count > 0
            ? verb.ResponseMessages
            : [new ResponseMessageDefinition { Code = "200", Message = "Successful operation", ResponseModel = verb.OutType }];
        foreach (var message in messages)
        {
            var response = new JsonObject { ["description"] = message.Message ?? string.Empty };
            if (message.Headers.Count > 0)
            {
                var headers = new JsonObject();
                foreach (var header in message.Headers)
                {
                    var headerNode = Object(("description", header.Description));
                    headerNode["schema"] = ParamSchema(header.DataType, header.DataFormat, null, header.AllowableValues, null);
                    headers[header.Name] = headerNode;
                }

                response["headers"] = headers;
            }

            var model = message.ResponseModel ?? (message.Code.StartsWith('2') && verb.ResponseMessages.Count > 0 ? verb.OutType : null)
                ?? (verb.ResponseMessages.Count == 0 ? verb.OutType : null);
            if (model is not null)
            {
                var content = new JsonObject();
                foreach (var mediaType in produces.Count > 0 ? produces : ["application/json"])
                {
                    content[mediaType] = new JsonObject { ["schema"] = Schema(model, schemas) };
                }

                response["content"] = content;
            }

            responses[message.Code] = response;
        }

        return responses;
    }

    private static JsonObject SecurityScheme(RestSecurityDefinition security) => security.Type switch
    {
        RestSecurityType.Basic => Object(("type", "http"), ("scheme", "basic"), ("description", security.Description)),
        RestSecurityType.Bearer => Object(("type", "http"), ("scheme", "bearer"), ("bearerFormat", security.Format), ("description", security.Description)),
        _ => Object(("type", "apiKey"), ("name", security.Name), ("in", security.In ?? "header"), ("description", security.Description)),
    };

    private static JsonObject Info(RestConfiguration configuration)
    {
        string? Property(string name) =>
            configuration.ApiProperties.TryGetValue("api." + name, out var value) || configuration.ApiProperties.TryGetValue(name, out value) ? value : null;

        var info = Object(("title", Property("title") ?? "Caravan REST API"), ("version", Property("version") ?? "1.0.0"), ("description", Property("description")));
        if (Property("termsOfService") is { } terms)
        {
            info["termsOfService"] = terms;
        }

        if (Property("contact.name") is not null || Property("contact.email") is not null || Property("contact.url") is not null)
        {
            info["contact"] = Object(("name", Property("contact.name")), ("email", Property("contact.email")), ("url", Property("contact.url")));
        }

        if (Property("license.name") is { } license)
        {
            info["license"] = Object(("name", license), ("url", Property("license.url")));
        }

        return info;
    }

    private static string ServerUrl(RestConfiguration configuration)
    {
        var contextPath = configuration.ContextPath is null ? string.Empty : PlatformHttpComponent.CombinePaths(configuration.ContextPath);
        if (configuration.Host is null)
        {
            return contextPath.Length == 0 ? "/" : contextPath;
        }

        var port = configuration.Port is { } p ? ":" + p.ToString(CultureInfo.InvariantCulture) : string.Empty;
        return $"{configuration.Scheme ?? "http"}://{configuration.Host}{port}{(contextPath == "/" ? string.Empty : contextPath)}";
    }

    private static IReadOnlyList<string> MediaTypes(string? list) => string.IsNullOrWhiteSpace(list)
        ? []
        : [.. list.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];

    private static IEnumerable<string> PathParameters(string path)
    {
        var start = path.IndexOf('{');
        while (start >= 0)
        {
            var end = path.IndexOf('}', start);
            if (end < 0)
            {
                yield break;
            }

            yield return path[(start + 1)..end].TrimStart('*').Split(':', '=', '?')[0];
            start = path.IndexOf('{', end);
        }
    }

    private static Type? ElementType(Type type)
    {
        if (type == typeof(string) || typeof(IDictionary).IsAssignableFrom(type))
        {
            return null;
        }

        if (type.IsArray)
        {
            return type.GetElementType();
        }

        var enumerable = type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>)
            ? type
            : type.GetInterfaces().FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>));
        if (enumerable is null)
        {
            return null;
        }

        var element = enumerable.GetGenericArguments()[0];
        return element.IsGenericType && element.GetGenericTypeDefinition() == typeof(KeyValuePair<,>) ? null : element;
    }

    private static bool IsNamedObject(Type type, JsonNode schema) =>
        !type.IsPrimitive && type != typeof(string) && type != typeof(decimal) && !type.IsEnum && !type.IsGenericType
        && type.Namespace?.StartsWith("System", StringComparison.Ordinal) != true
        && schema is JsonObject o && o["type"] switch
        {
            JsonValue value => value.ToString() == "object",
            JsonArray values => values.Any(v => v?.ToString() == "object"),
            _ => false,
        };

    private static string SchemaName(Type type) => type.IsNested ? $"{type.DeclaringType!.Name}.{type.Name}" : type.Name;

    private static JsonObject Object(params (string Name, string? Value)[] properties)
    {
        var node = new JsonObject();
        foreach (var (name, value) in properties)
        {
            if (value is not null)
            {
                node[name] = value;
            }
        }

        return node;
    }

    private static JsonObject ToObject(IEnumerable<KeyValuePair<string, JsonNode>> entries)
    {
        var node = new JsonObject();
        foreach (var (name, value) in entries)
        {
            node[name] = value;
        }

        return node;
    }
}
