using Caravan.DataTypes;

namespace Caravan.Dsl.Yaml.Catalog;

/// <summary>
/// The top-level <c>transformers</c> and <c>validators</c> (a Caravan extension using Camel's element names): data type transformers and
/// validators added to the context, used by route <c>inputType</c>/<c>outputType</c> and <c>transform</c> with <c>toType</c>.
/// </summary>
internal static class DataTypeCatalog
{
    private static readonly string[] TransformerOptionNames = ["fromType", "toType", "name", "scheme"];

    private static readonly OptionSpec[] TransformerOptions =
    [
        Opt.Str("fromType", "The source data type (scheme:name, or a scheme); absent for any."),
        Opt.Str("toType", "The target data type (scheme:name, or a scheme)."),
        Opt.Str("name", "A name for the transformer."),
    ];

    /// <summary>A transformer: exactly one of <c>dataFormatTransformer</c>, <c>endpointTransformer</c>, <c>customTransformer</c>.</summary>
    public static readonly ObjectSpec Transformer = new()
    {
        Options =
        [
            Opt.Object("dataFormatTransformer", "Marshals to (or unmarshals from) the scheme with a data format: one key naming it, with its options.", new ObjectSpec
            {
                Options = [.. TransformerOptions, Opt.Str("scheme", "The scheme of the serialized form (default: the data format name)."), .. DataFormatCatalog.Options],
                DynamicOptions = DataFormatCatalog.IsDataFormat,
            }),
            Opt.Object("endpointTransformer", "Transforms by calling an endpoint (InOut) and taking its reply.", new ObjectSpec
            {
                Options = [.. TransformerOptions, Opt.Str("uri", "The endpoint URI.", required: true).Alias("ref")],
            }),
            Opt.Object("customTransformer", "An ITransformer bean (ref) or type (className).", new ObjectSpec
            {
                Options = [.. TransformerOptions, Opt.Ref("ref", "The transformer"), Opt.Type("className", "The transformer type (created with its default constructor).")],
            }),
            Opt.Unsupported("loadTransformer", "load transformers from the classpath are not supported; declare them"),
        ],
    };

    /// <summary>A validator: exactly one of <c>predicateValidator</c>, <c>endpointValidator</c>, <c>customValidator</c>.</summary>
    public static readonly ObjectSpec Validator = new()
    {
        Options =
        [
            Opt.Object("predicateValidator", "Validates with a predicate.", new ObjectSpec
            {
                Expression = ExpressionSlot.Predicate,
                Options = [Opt.Str("type", "The data type validated.", required: true)],
            }),
            Opt.Object("endpointValidator", "Validates by calling an endpoint; invalid when the call fails.", new ObjectSpec
            {
                Options = [Opt.Str("type", "The data type validated.", required: true), Opt.Str("uri", "The endpoint URI.", required: true).Alias("ref")],
            }),
            Opt.Object("customValidator", "An IValidator bean (ref) or type (className).", new ObjectSpec
            {
                Options = [Opt.Str("type", "The data type validated."), Opt.Ref("ref", "The validator"), Opt.Type("className", "The validator type (created with its default constructor).")],
            }),
        ],
    };

    /// <summary>Builds a transformer.</summary>
    public static ITransformer BuildTransformer(YamlObject item)
    {
        var (kind, o) = Single(item);
        var from = Parse(o.String("fromType"));
        var to = Parse(o.String("toType"));
        var name = o.String("name");
        if (to is null && name is null)
        {
            throw o.Context.Error(o.Node, $"'{kind}' needs toType or name");
        }

        switch (kind)
        {
            case "dataFormatTransformer":
                {
                    var (dataFormatName, dataFormat) = DataFormatCatalog.Resolve(o, TransformerOptionNames);
                    var scheme = o.String("scheme") ?? dataFormatName;
                    return dataFormat is null
                        ? new DataFormatTransformer(from, to, dataFormatName, scheme) { Name = name }
                        : new DataFormatTransformer(from, to, dataFormat, scheme) { Name = name };
                }

            case "endpointTransformer":
                return new EndpointTransformer(from, to, o.RequiredString("uri")) { Name = name };
            default:
                return Custom<ITransformer>(o, kind);
        }
    }

    /// <summary>Builds a validator.</summary>
    public static IValidator BuildValidator(YamlObject item)
    {
        var (kind, o) = Single(item);
        return kind switch
        {
            "predicateValidator" => new PredicateValidator(DataType.Parse(o.RequiredString("type")), o.InlinePredicate()),
            "endpointValidator" => new EndpointValidator(DataType.Parse(o.RequiredString("type")), o.RequiredString("uri")),
            _ => Custom<IValidator>(o, kind),
        };
    }

    private static (string Kind, YamlObject Options) Single(YamlObject item)
    {
        var names = item.Names.ToList();
        if (names.Count != 1)
        {
            throw item.Context.Error(item.Node, $"Each item needs exactly one of: {string.Join(", ", item.Spec.Options.Where(o => o.Unsupported is null).Select(o => o.Name))}");
        }

        return (names[0], item.Object(names[0])!);
    }

    private static DataType? Parse(string? text) => text is null ? null : DataType.Parse(text);

    private static T Custom<T>(YamlObject o, string kind)
        where T : class
    {
        if (o.String("ref") is { } reference)
        {
            return o.Context.Context.Registry.Lookup(reference) as T
                ?? throw o.Error("ref", $"No {typeof(T).Name} bean named {reference}");
        }

        var type = o.Type("className") ?? throw o.Context.Error(o.Node, $"'{kind}' needs ref or className");
        return Activator.CreateInstance(type) as T ?? throw o.Error("className", $"{type.FullName} is not an {typeof(T).Name}");
    }
}
