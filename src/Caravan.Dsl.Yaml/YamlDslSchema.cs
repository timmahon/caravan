using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Caravan.Dsl.Yaml.Catalog;

namespace Caravan.Dsl.Yaml;

/// <summary>
/// The JSON schema (draft-07) of the YAML DSL, generated from the same catalog the loader validates against, so it never drifts from what
/// loads. Point your editor at it (e.g. with a <c># yaml-language-server: $schema=...</c> comment) for completion and validation. The
/// repository keeps a copy in <c>schema/caravan-yaml-dsl.json</c>, checked by a test. Only the camelCase names are listed (the loader also
/// accepts kebab-case: normalize keys to camelCase before validating) and only built-in languages and data formats.
/// </summary>
public static class YamlDslSchema
{
    /// <summary>The file name of the committed schema.</summary>
    public const string FileName = "caravan-yaml-dsl.json";

    /// <summary>Generates the schema.</summary>
    /// <returns>The schema as indented JSON (with a trailing newline).</returns>
    public static string Generate() => new Generator().Run();

    /// <summary>Builds the schema document.</summary>
    private sealed class Generator
    {
        private static readonly JsonSerializerOptions Options = new()
        {
            WriteIndented = true,
            NewLine = "\n",
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        private readonly Dictionary<ObjectSpec, string> _named = new(ReferenceEqualityComparer.Instance)
        {
            [ModelCatalog.RedeliveryPolicy] = "redeliveryPolicy",
            [ModelCatalog.ErrorHandler] = "errorHandler",
            [ModelCatalog.OnException] = "onException",
            [ModelCatalog.OnCompletion] = "onCompletion",
            [ModelCatalog.Intercept] = "intercept",
            [ModelCatalog.InterceptFrom] = "interceptFrom",
            [ModelCatalog.InterceptSendToEndpoint] = "interceptSendToEndpoint",
            [TopLevelCatalog.From] = "from",
            [TopLevelCatalog.Route] = "route",
            [TopLevelCatalog.Bean] = "bean",
            [TopLevelCatalog.RouteTemplate] = "routeTemplate",
            [TopLevelCatalog.TemplatedRoute] = "templatedRoute",
            [TopLevelCatalog.RouteConfiguration] = "routeConfiguration",
            [RestCatalog.Rest] = "rest",
            [RestCatalog.Verb] = "restVerb",
            [RestCatalog.Configuration] = "restConfiguration",
        };

        private readonly JsonObject _definitions = [];

        public string Run()
        {
            foreach (var (spec, name) in _named.OrderBy(n => n.Value, StringComparer.Ordinal))
            {
                _definitions[name] = ObjectSchema(spec, top: true);
            }

            foreach (var (name, (description, spec)) in LanguageCatalog.Languages.OrderBy(l => l.Key, StringComparer.Ordinal))
            {
                var schema = ObjectSchema(spec, top: true);
                schema["description"] = description;
                _definitions["language." + name] = schema;
            }

            var languages = new JsonObject();
            foreach (var name in LanguageCatalog.Languages.Keys.Order(StringComparer.Ordinal))
            {
                languages[name] = Ref("language." + name);
            }

            languages["expression"] = Ref("expression");
            _definitions["expression"] = new JsonObject
            {
                ["description"] = "One expression: a language key (simple, constant, header...) with its text or options, or expression: { ... }.",
                ["type"] = "object",
                ["minProperties"] = 1,
                ["maxProperties"] = 1,
                ["properties"] = languages,
                ["additionalProperties"] = false,
            };
            _definitions["predicate"] = new JsonObject
            {
                ["description"] = "A predicate: an expression object, or true/false.",
                ["anyOf"] = new JsonArray(new JsonObject { ["type"] = "boolean" }, Ref("expression")),
            };

            var steps = new JsonObject();
            foreach (var (name, spec) in StepCatalog.Steps.OrderBy(s => s.Key, StringComparer.Ordinal))
            {
                var schema = ObjectSchema(spec, top: true);
                schema["description"] = spec.Description;
                _definitions["step." + name] = schema;
                steps[name] = Ref("step." + name);
            }

            _definitions["step"] = new JsonObject
            {
                ["description"] = "A step: an object with one key, the step name.",
                ["type"] = "object",
                ["minProperties"] = 1,
                ["maxProperties"] = 1,
                ["properties"] = steps,
                ["additionalProperties"] = false,
            };
            _definitions["steps"] = new JsonObject
            {
                ["type"] = "array",
                ["items"] = Ref("step"),
            };

            var root = (JsonObject)ObjectSchema(TopLevelCatalog.Root, top: true, nullable: false);
            root["minProperties"] = 1;
            root["maxProperties"] = 1;
            var document = new JsonObject
            {
                ["$schema"] = "http://json-schema.org/draft-07/schema#",
                ["title"] = "Caravan YAML DSL",
                ["description"] = "Routes, route templates, route configurations and beans in Apache Camel's YAML DSL, as loaded by Caravan.Dsl.Yaml. Generated by YamlDslSchema.Generate(); do not edit.",
                ["type"] = "array",
                ["items"] = root,
                ["definitions"] = Sorted(_definitions),
            };
            return document.ToJsonString(Options) + "\n";
        }

        private static JsonObject Sorted(JsonObject source)
        {
            var sorted = new JsonObject();
            foreach (var (key, value) in source.OrderBy(p => p.Key, StringComparer.Ordinal).ToList())
            {
                source.Remove(key);
                sorted[key] = value;
            }

            return sorted;
        }

        private static JsonObject Ref(string name) => new() { ["$ref"] = "#/definitions/" + name };

        private JsonNode ObjectSchema(ObjectSpec spec, bool top = false, bool nullable = true)
        {
            if (!top && _named.TryGetValue(spec, out var name))
            {
                return Ref(name);
            }

            var properties = new JsonObject();
            var required = new JsonArray();
            var allOf = new JsonArray();
            foreach (var option in spec.Options.Where(o => o.Unsupported is null))
            {
                var schema = OptionSchema(option);
                schema["description"] = option.Description;
                properties[option.Name] = schema;
                foreach (var alias in option.Aliases)
                {
                    var aliasSchema = OptionSchema(option);
                    aliasSchema["description"] = $"Alias of {option.Name}.";
                    properties[alias] = aliasSchema;
                }

                if (option.Required)
                {
                    if (option.Aliases.Count == 0)
                    {
                        required.Add(option.Name);
                    }
                    else
                    {
                        allOf.Add(new JsonObject
                        {
                            ["anyOf"] = new JsonArray([.. option.Aliases.Prepend(option.Name).Select(n => (JsonNode)new JsonObject { ["required"] = new JsonArray(n) })]),
                        });
                    }
                }
            }

            if (spec.CommonOptions)
            {
                foreach (var common in new[] { Opt.Id, Opt.Description, Opt.Disabled })
                {
                    var schema = OptionSchema(common);
                    schema["description"] = common.Description;
                    properties[common.Name] = schema;
                }
            }

            if (spec.Expression != ExpressionSlot.None)
            {
                foreach (var language in LanguageCatalog.Languages.Keys.Order(StringComparer.Ordinal))
                {
                    properties[language] = Ref("language." + language);
                }

                properties["expression"] = Ref("expression");
                if (spec.ExpressionRequired)
                {
                    allOf.Add(new JsonObject
                    {
                        ["anyOf"] = new JsonArray([.. LanguageCatalog.Languages.Keys.Order(StringComparer.Ordinal).Append("expression")
                            .Select(n => (JsonNode)new JsonObject { ["required"] = new JsonArray(n) })]),
                    });
                }
            }

            var result = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = properties,
                ["additionalProperties"] = false,
            };
            if (required.Count > 0)
            {
                result["required"] = required;
            }

            if (allOf.Count > 0)
            {
                result["allOf"] = allOf;
            }

            var alternatives = new JsonArray();
            if (spec.InlineOption is not null)
            {
                alternatives.Add(new JsonObject { ["type"] = new JsonArray("string", "number", "boolean") });
            }

            if (nullable && required.Count == 0 && allOf.Count == 0)
            {
                alternatives.Add(new JsonObject { ["type"] = "null" });
            }

            if (alternatives.Count == 0)
            {
                return result;
            }

            alternatives.Add(result);
            return new JsonObject { ["anyOf"] = alternatives };
        }

        private JsonObject OptionSchema(OptionSpec option) => option.Kind switch
        {
            OptionKind.String => Type("string", "number", "boolean"),
            OptionKind.Integer or OptionKind.Duration => Type("integer", "string"),
            OptionKind.Number => Type("number", "string"),
            OptionKind.Boolean => Type("boolean", "string"),
            OptionKind.Enum => new JsonObject { ["type"] = "string", ["enum"] = new JsonArray([.. option.EnumValues!.Select(v => (JsonNode)v)]) },
            OptionKind.Type => new JsonObject { ["type"] = "string" },
            OptionKind.TypeList or OptionKind.StringList => new JsonObject
            {
                ["anyOf"] = new JsonArray(
                    new JsonObject { ["type"] = "string" },
                    new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "string" } }),
            },
            OptionKind.Expression => Ref("expression"),
            OptionKind.Predicate => Ref("predicate"),
            OptionKind.Steps => Ref("steps"),
            OptionKind.Object => Wrap(ObjectSchema(option.Nested!)),
            OptionKind.ObjectList => new JsonObject { ["type"] = "array", ["items"] = Item(option) },
            OptionKind.Map => new JsonObject { ["type"] = new JsonArray("object", "null") },
            _ => [],
        };

        private JsonNode Item(OptionSpec option)
        {
            var item = ObjectSchema(option.Nested!);
            if (option.ItemWrapper is not { } wrapper)
            {
                return item;
            }

            return new JsonObject
            {
                ["anyOf"] = new JsonArray(
                    item,
                    new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject { [wrapper] = ObjectSchema(option.Nested!) },
                        ["required"] = new JsonArray(wrapper),
                        ["additionalProperties"] = false,
                    }),
            };
        }

        private static JsonObject Wrap(JsonNode node) => node as JsonObject ?? new JsonObject { ["allOf"] = new JsonArray(node) };

        private static JsonObject Type(params string[] types) => new() { ["type"] = new JsonArray([.. types.Select(t => (JsonNode)t)]) };
    }
}
