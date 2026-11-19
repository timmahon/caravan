using System.Globalization;
using System.Text.Json.Nodes;
using YamlDotNet.RepresentationModel;

namespace Caravan.Dsl.Yaml.Tests;

/// <summary>
/// A small JSON schema (draft-07) validator for the keywords the generated schema uses ($ref, type, properties, additionalProperties,
/// required, items, anyOf, allOf, enum, minProperties, maxProperties), and a YAML to JSON converter typing plain scalars as YAML 1.2 does.
/// </summary>
public sealed class MiniSchemaValidator(JsonObject schema)
{
    public static JsonNode? ToJson(YamlNode node) => node switch
    {
        YamlMappingNode mapping => new JsonObject(mapping.Children.Select(e => KeyValuePair.Create(CamelCase(((YamlScalarNode)e.Key).Value!), ToJson(e.Value)))),
        YamlSequenceNode sequence => new JsonArray([.. sequence.Children.Select(ToJson)]),
        YamlScalarNode { Style: YamlDotNet.Core.ScalarStyle.Plain } scalar => Plain(scalar.Value ?? string.Empty),
        YamlScalarNode scalar => JsonValue.Create(scalar.Value),
        _ => throw new NotSupportedException(),
    };

    /// <summary>The schema lists camelCase names; the loader also accepts kebab-case ones.</summary>
    public static string CamelCase(string key) =>
        string.Concat(key.Split('-').Select((part, i) => i == 0 || part.Length == 0 ? part : char.ToUpperInvariant(part[0]) + part[1..]));

    public List<string> Validate(JsonNode? instance)
    {
        var errors = new List<string>();
        Validate(schema, instance, "$", errors);
        return errors;
    }

    private static JsonNode? Plain(string text) => text switch
    {
        "" or "~" or "null" => null,
        "true" => JsonValue.Create(true),
        "false" => JsonValue.Create(false),
        _ when long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l) => JsonValue.Create(l),
        _ when double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) => JsonValue.Create(d),
        _ => JsonValue.Create(text),
    };

    private void Validate(JsonNode schemaNode, JsonNode? instance, string path, List<string> errors)
    {
        var s = (JsonObject)schemaNode;
        if (s["$ref"] is { } reference)
        {
            var name = reference.GetValue<string>()["#/definitions/".Length..];
            Validate(schema["definitions"]![name]!, instance, path, errors);
            return;
        }

        if (s["type"] is { } type)
        {
            var types = type is JsonArray array ? array.Select(t => t!.GetValue<string>()).ToList() : [type.GetValue<string>()];
            if (!types.Any(t => Is(t, instance)))
            {
                errors.Add($"{path}: expected {string.Join("|", types)}");
                return;
            }
        }

        if (s["enum"] is JsonArray values && !values.Any(v => v!.GetValue<string>() == instance?.GetValue<object>()?.ToString()))
        {
            errors.Add($"{path}: not one of the enum values");
        }

        if (s["anyOf"] is JsonArray anyOf)
        {
            var results = anyOf.Select(option =>
            {
                var optionErrors = new List<string>();
                Validate(option!, instance, path, optionErrors);
                return optionErrors;
            }).ToList();
            if (results.All(r => r.Count > 0))
            {
                errors.Add($"{path}: matches none of anyOf ({string.Join("; ", results.OrderBy(r => r.Count).First())})");
            }
        }

        if (s["allOf"] is JsonArray allOf)
        {
            foreach (var option in allOf)
            {
                Validate(option!, instance, path, errors);
            }
        }

        if (instance is JsonObject obj)
        {
            if (s["minProperties"] is { } min && obj.Count < min.GetValue<int>())
            {
                errors.Add($"{path}: too few properties");
            }

            if (s["maxProperties"] is { } max && obj.Count > max.GetValue<int>())
            {
                errors.Add($"{path}: too many properties");
            }

            if (s["required"] is JsonArray required)
            {
                errors.AddRange(required.Select(r => r!.GetValue<string>()).Where(r => !obj.ContainsKey(r)).Select(r => $"{path}: missing {r}"));
            }

            var properties = s["properties"] as JsonObject;
            foreach (var (key, value) in obj)
            {
                if (properties?[key] is { } propertySchema)
                {
                    Validate(propertySchema, value, $"{path}.{key}", errors);
                }
                else if (s["additionalProperties"] is JsonValue additional && !additional.GetValue<bool>())
                {
                    errors.Add($"{path}: unexpected property {key}");
                }
            }
        }

        if (instance is JsonArray items && s["items"] is { } itemSchema)
        {
            for (var i = 0; i < items.Count; i++)
            {
                Validate(itemSchema, items[i], $"{path}[{i}]", errors);
            }
        }
    }

    private static bool Is(string type, JsonNode? instance) => type switch
    {
        "null" => instance is null,
        "object" => instance is JsonObject,
        "array" => instance is JsonArray,
        "string" => instance is JsonValue v && v.TryGetValue<string>(out _),
        "boolean" => instance is JsonValue v && v.TryGetValue<bool>(out _),
        "integer" => instance is JsonValue v && v.TryGetValue<long>(out _),
        "number" => instance is JsonValue v && (v.TryGetValue<double>(out _) || v.TryGetValue<long>(out _)),
        _ => throw new NotSupportedException(type),
    };
}
