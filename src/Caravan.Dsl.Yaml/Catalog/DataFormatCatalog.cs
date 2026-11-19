using System.Reflection;
using Caravan.DataFormat;
using Caravan.Spi;
using Caravan.Support;

namespace Caravan.Dsl.Yaml.Catalog;

/// <summary>
/// The data formats of <c>marshal</c>/<c>unmarshal</c>: <c>marshal: { json: { unmarshalType: ..., prettyPrint: true } }</c>. The built-in
/// ones are described from their public properties (for validation and the schema); data formats registered in the context are accepted too.
/// Options are bound to a data format instance resolved when the routes are loaded; without options the name is resolved when the route is
/// created.
/// </summary>
internal static class DataFormatCatalog
{
    private static readonly Dictionary<string, string> CamelAliases = new(StringComparer.Ordinal)
    {
        ["unmarshalTypeName"] = "unmarshalType",
    };

    private static readonly (string Name, Type Type, string Description)[] BuiltIns =
    [
        ("json", typeof(JsonDataFormat), "JSON with System.Text.Json."),
        ("jackson", typeof(JsonDataFormat), "JSON (Camel's Jackson name)."),
        ("xml", typeof(XmlDataFormat), "XML with XmlSerializer."),
        ("jaxb", typeof(XmlDataFormat), "XML (Camel's JAXB name)."),
        ("csv", typeof(CsvDataFormat), "CSV (RFC 4180)."),
        ("base64", typeof(Base64DataFormat), "Base64."),
        ("gzipDeflater", typeof(GzipDeflaterDataFormat), "GZip compression."),
        ("gzip", typeof(GzipDeflaterDataFormat), "GZip compression."),
        ("zipDeflater", typeof(ZipDeflaterDataFormat), "Zlib (Java Deflater) compression."),
        ("string", typeof(StringDataFormat), "Text in a charset."),
    ];

    private static readonly string[] IgnoredKeys = ["id", "library"];

    /// <summary>One option per built-in data format, plus <c>custom</c>.</summary>
    public static readonly IReadOnlyList<OptionSpec> Options =
    [
        .. BuiltIns.Select(b => Opt.Object(b.Name, b.Description, Describe(b.Type))),
        Opt.Object(
            "custom",
            "A data format bound in the registry or registered in the context.",
            new ObjectSpec { InlineOption = "ref", Options = [Opt.Ref("ref", "The data format", required: true)] }),
    ];

    /// <summary>Whether <paramref name="name"/> names a data format known to the context (for data formats that are not built in).</summary>
    public static bool IsDataFormat(ICaravanContext context, string name) =>
        context.DataFormats.Names.Contains(name, StringComparer.OrdinalIgnoreCase) || context.Registry.Lookup(name) is IDataFormat;

    /// <summary>The data format of a <c>marshal</c>/<c>unmarshal</c> step: its name and, when options are given, the configured instance.</summary>
    public static (string Name, IDataFormat? DataFormat) Resolve(YamlObject options, params string[] otherOptions)
    {
        var dataFormats = options.Names.Where(n => n is not ("allowNullBody" or "id" or "description" or "disabled") && !otherOptions.Contains(n))
            .Select(n => (Name: n, Key: (YamlDotNet.RepresentationModel.YamlNode?)null, Value: options.Raw(n)!))
            .Concat(options.DynamicEntries.Select(e => (e.Name, Key: (YamlDotNet.RepresentationModel.YamlNode?)e.Key, e.Value)))
            .ToList();
        if (dataFormats.Count != 1)
        {
            throw options.Context.Error(
                options.Node,
                $"'{options.Element}' needs exactly one data format, e.g. {options.Element}: {{ json: {{}} }}" +
                (dataFormats.Count == 0 ? string.Empty : $" (found: {string.Join(", ", dataFormats.Select(d => d.Name))})"));
        }

        var (name, _, value) = dataFormats[0];
        if (name == "custom")
        {
            return (options.Object("custom")!.RequiredString("ref"), null);
        }

        var entries = Entries(options, name, value);
        if (entries.Count == 0)
        {
            return (name, null);
        }

        IDataFormat dataFormat;
        try
        {
            dataFormat = options.Context.Context.ResolveDataFormat(name);
        }
        catch (NoSuchDataFormatException e)
        {
            throw options.Context.Error(value, e.Message, e);
        }

        foreach (var (key, keyNode, text) in entries)
        {
            try
            {
                if (!PropertyBinder.TrySetProperty(options.Context.Context, dataFormat, key, text))
                {
                    var known = Properties(dataFormat.GetType()).Select(p => char.ToLowerInvariant(p.Name[0]) + p.Name[1..]).ToList();
                    var hint = NameConverter.Closest(key, known) is { } closest ? $" Did you mean '{closest}'?" : string.Empty;
                    throw options.Context.Error(keyNode, $"Unknown option '{key}' of data format '{name}'.{hint} Valid options: {string.Join(", ", known)}");
                }
            }
            catch (Exception e) when (e is ArgumentException or CaravanException && e is not YamlRouteDefinitionException)
            {
                throw options.Context.Error(keyNode, $"Invalid option '{key}' of data format '{name}': {e.Message}", e);
            }
        }

        return (name, dataFormat);
    }

    private static List<(string Key, YamlDotNet.RepresentationModel.YamlNode KeyNode, string? Value)> Entries(
        YamlObject options, string name, YamlDotNet.RepresentationModel.YamlNode value)
    {
        if (YamlObject.IsNull(value))
        {
            return [];
        }

        if (value is not YamlDotNet.RepresentationModel.YamlMappingNode mapping)
        {
            throw options.Context.Error(value, $"The options of data format '{name}' must be a mapping, e.g. {name}: {{ }}");
        }

        var result = new List<(string, YamlDotNet.RepresentationModel.YamlNode, string?)>();
        foreach (var (keyNode, item) in mapping.Children)
        {
            var key = NameConverter.ToCamelCase(((YamlDotNet.RepresentationModel.YamlScalarNode)keyNode).Value ?? string.Empty);
            key = CamelAliases.GetValueOrDefault(key, key);
            if (IgnoredKeys.Contains(key))
            {
                continue;
            }

            if (item is not YamlDotNet.RepresentationModel.YamlScalarNode scalar)
            {
                throw options.Context.Error(item, $"Option '{key}' of data format '{name}' must be a scalar");
            }

            var text = YamlObject.IsNull(scalar) ? null : options.Context.Text(scalar.Value ?? string.Empty);
            if (text is not null && PropertyType(options, name, key) == typeof(Type) && TypeResolver.Resolve(options.Context.Context, text) is { } type)
            {
                text = type.AssemblyQualifiedName;
            }

            result.Add((key, keyNode, text));
        }

        return result;
    }

    private static Type? PropertyType(YamlObject options, string name, string key)
    {
        var type = BuiltIns.FirstOrDefault(b => b.Name == name).Type;
        return type is null ? null : Properties(type).FirstOrDefault(p => string.Equals(p.Name, key, StringComparison.OrdinalIgnoreCase))?.PropertyType is { } t
            ? Nullable.GetUnderlyingType(t) ?? t
            : null;
    }

    private static IEnumerable<PropertyInfo> Properties(Type type) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.SetMethod is { IsPublic: true } && p.GetIndexParameters().Length == 0);

    private static ObjectSpec Describe(Type type)
    {
        var options = new List<OptionSpec>();
        foreach (var property in Properties(type).OrderBy(p => p.Name, StringComparer.Ordinal))
        {
            var propertyType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
            var name = char.ToLowerInvariant(property.Name[0]) + property.Name[1..];
            var description = $"The {property.Name} property of {type.Name}.";
            OptionSpec? option = propertyType switch
            {
                _ when propertyType == typeof(bool) => Opt.Bool(name, description),
                _ when propertyType == typeof(int) || propertyType == typeof(long) => Opt.Int(name, description),
                _ when propertyType == typeof(string) || propertyType == typeof(char) => Opt.Str(name, description),
                _ when propertyType == typeof(Type) => Opt.Type(name, description),
                _ when propertyType.IsEnum => Opt.Enum(name, description, Enum.GetNames(propertyType)),
                _ => null,
            };
            if (option is not null)
            {
                options.Add(name == "unmarshalType" ? option.Alias("unmarshalTypeName") : option);
            }
        }

        options.Add(Opt.Ignored("id", OptionKind.String, "data formats have no id"));
        if (type == typeof(JsonDataFormat))
        {
            options.Add(Opt.Ignored("library", OptionKind.String, "System.Text.Json is used"));
        }

        return new ObjectSpec { Options = options };
    }
}
