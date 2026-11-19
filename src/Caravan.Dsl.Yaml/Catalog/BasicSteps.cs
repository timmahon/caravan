using Caravan.Builder;
using Caravan.Language;
using Caravan.Model;

namespace Caravan.Dsl.Yaml.Catalog;

/// <summary>Steps sending to endpoints, calling processors and beans, and changing the message.</summary>
internal static class BasicSteps
{
    private static readonly OptionSpec Uri = Opt.Str("uri", "The endpoint URI.", required: true);
    private static readonly OptionSpec Parameters = Opt.Map("parameters", "Endpoint options appended to the URI as query parameters.");
    private static readonly OptionSpec Pattern = Opt.Enum("pattern", "The exchange pattern used for the send.", "InOnly", "InOut");
    private static readonly OptionSpec VariableSend = Opt.Str("variableSend", "A variable whose value is sent as the body instead of the message body.");
    private static readonly OptionSpec VariableReceive = Opt.Str("variableReceive", "A variable receiving the reply (body; headers as name.header.key); the message is left unchanged.");

    /// <summary>The steps.</summary>
    public static IEnumerable<StepSpec> All =>
    [
        new()
        {
            Name = "to",
            Description = "Sends the exchange to an endpoint.",
            InlineOption = "uri",
            Options = [Uri, Parameters, Pattern, VariableSend, VariableReceive],
            Build = o => new ToDefinition(EndpointUris.Build(o), o.Enum<ExchangePattern>("pattern"))
            {
                VariableSend = o.String("variableSend"),
                VariableReceive = o.String("variableReceive"),
            },
        },
        new()
        {
            Name = "toD",
            Description = "Sends the exchange to an endpoint computed per exchange (a Simple expression).",
            InlineOption = "uri",
            Options =
            [
                Uri, Parameters, Pattern,
                Opt.Int("cacheSize", "Size of the cache of producers."),
                Opt.Bool("ignoreInvalidEndpoint", "Ignores URIs that do not resolve to an endpoint."),
                Opt.Ignored("allowOptimisedComponents", OptionKind.Boolean, "no optimised components"),
                Opt.Ignored("autoStartComponents", OptionKind.Boolean, "components start on use"),
                VariableSend, VariableReceive,
            ],
            Build = o =>
            {
                var definition = new ToDynamicDefinition(Simple(o, EndpointUris.Build(o)))
                {
                    Pattern = o.Enum<ExchangePattern>("pattern"),
                    VariableSend = o.String("variableSend"),
                    VariableReceive = o.String("variableReceive"),
                };
                definition.CacheSize = o.Int("cacheSize") ?? definition.CacheSize;
                definition.IgnoreInvalidEndpoint = o.Bool("ignoreInvalidEndpoint") ?? false;
                return definition;
            },
        },
        new()
        {
            Name = "inOnly",
            Description = "Camel 3: sends with the InOnly pattern (use to with pattern: InOnly).",
            InlineOption = "uri",
            Options = [Uri, Parameters],
            Build = o => new ToDefinition(EndpointUris.Build(o), ExchangePattern.InOnly),
        },
        new()
        {
            Name = "inOut",
            Description = "Camel 3: sends with the InOut pattern (use to with pattern: InOut).",
            InlineOption = "uri",
            Options = [Uri, Parameters],
            Build = o => new ToDefinition(EndpointUris.Build(o), ExchangePattern.InOut),
        },
        new()
        {
            Name = "kamelet",
            Description = "Calls a route template as a Kamelet (kamelet:name[/routeId]?parameters).",
            InlineOption = "name",
            Options = [Opt.Str("name", "The template id, optionally /routeId and ?parameters.", required: true), Parameters],
            Build = o => new ToDefinition(EndpointUris.Build(o, "name", "kamelet:")),
        },
        new()
        {
            Name = "process",
            Description = "Calls a processor bound in the registry.",
            InlineOption = "ref",
            Options = [Opt.Ref("ref", "The processor", required: true)],
            Build = o => Dsl.Single(s => s.ProcessRef(o.RequiredString("ref"))),
        },
        new()
        {
            Name = "bean",
            Description = "Calls a bean method; the result becomes the body.",
            InlineOption = "ref",
            Options =
            [
                Opt.Ref("ref", "The bean"),
                Opt.Type("beanType", "The bean type (instantiated, or its static methods called)."),
                Opt.Str("method", "The method name or signature."),
                Opt.Enum("scope", "The bean scope.", "Singleton", "Request", "Prototype"),
            ],
            Build = o =>
            {
                var definition = new BeanDefinition
                {
                    Ref = o.String("ref"),
                    BeanType = o.Type("beanType"),
                    Method = o.String("method"),
                    Scope = o.Enum<BeanScope>("scope") ?? BeanScope.Singleton,
                };
                return (definition.Ref is null) == (definition.BeanType is null)
                    ? throw o.Context.Error(o.Node, "bean needs either ref or beanType")
                    : definition;
            },
        },
        new()
        {
            Name = "setBody",
            Description = "Replaces the body with the value of an expression.",
            Expression = ExpressionSlot.Expression,
            Build = o => new SetBodyDefinition(o.InlineExpression()),
        },
        new()
        {
            Name = "transform",
            Description = "Replaces the body with the value of an expression, or transforms the message to a data type (toType) with the context's transformers.",
            Expression = ExpressionSlot.Expression,
            ExpressionRequired = false,
            Options =
            [
                Opt.Str("fromType", "The current data type (scheme:name); default: the message's."),
                Opt.Str("toType", "The data type to transform to (scheme:name), instead of an expression."),
            ],
            Build = o =>
            {
                var expression = o.OptionalInlineExpression();
                var toType = o.String("toType");
                return (expression, toType) switch
                {
                    ({ } e, null) when !o.Has("fromType") => new TransformDefinition(e),
                    (null, { } type) => new TransformDefinition(
                        Caravan.DataTypes.DataType.Parse(type),
                        o.String("fromType") is { } from ? Caravan.DataTypes.DataType.Parse(from) : null),
                    _ => throw o.Context.Error(o.Node, "transform needs either an expression or toType (with an optional fromType)"),
                };
            },
        },
        new()
        {
            Name = "setHeader",
            Description = "Sets a header to the value of an expression.",
            Expression = ExpressionSlot.Expression,
            Options = [Opt.Str("name", "The header name.", required: true)],
            Build = o => new SetHeaderDefinition(o.RequiredString("name"), o.InlineExpression()),
        },
        new()
        {
            Name = "setHeaders",
            Description = "Sets several headers, in order.",
            Options =
            [
                Opt.Objects(
                    "headers",
                    "The headers: a name and an expression each.",
                    new ObjectSpec { Expression = ExpressionSlot.Expression, Options = [Opt.Str("name", "The header name.", required: true)] },
                    required: true),
            ],
            Build = o =>
            {
                var definition = new SetHeadersDefinition();
                foreach (var header in o.Objects("headers"))
                {
                    definition.Headers.Add(new(header.RequiredString("name"), header.InlineExpression()));
                }

                return definition;
            },
        },
        new()
        {
            Name = "setProperty",
            Description = "Sets an exchange property to the value of an expression.",
            Expression = ExpressionSlot.Expression,
            Options = [Opt.Str("name", "The property name.", required: true)],
            Build = o => new SetPropertyDefinition(o.RequiredString("name"), o.InlineExpression()),
        },
        new()
        {
            Name = "setVariable",
            Description = "Sets a variable (exchange; global:name; route:name) to the value of an expression.",
            Expression = ExpressionSlot.Expression,
            Options = [Opt.Str("name", "The variable name.", required: true)],
            Build = o => new SetVariableDefinition(o.RequiredString("name"), o.InlineExpression()),
        },
        new()
        {
            Name = "setVariables",
            Description = "Sets several variables, in order.",
            Options =
            [
                Opt.Objects(
                    "variables",
                    "The variables: a name and an expression each.",
                    new ObjectSpec { Expression = ExpressionSlot.Expression, Options = [Opt.Str("name", "The variable name.", required: true)] },
                    required: true),
            ],
            Build = o =>
            {
                var definition = new SetVariablesDefinition();
                foreach (var variable in o.Objects("variables"))
                {
                    definition.Variables.Add(new(variable.RequiredString("name"), variable.InlineExpression()));
                }

                return definition;
            },
        },
        new()
        {
            Name = "removeVariable",
            Description = "Removes the variables matching a name or pattern (*, wildcard, regular expression; global:* for global ones).",
            InlineOption = "name",
            Options = [Opt.Str("name", "The variable name or pattern.", required: true)],
            Build = o => new RemoveVariableDefinition(o.RequiredString("name")),
        },
        new()
        {
            Name = "convertVariableTo",
            Description = "Converts a variable to a type.",
            Options =
            [
                Opt.Str("name", "The variable name.", required: true),
                Opt.Type("type", "The target type.", required: true),
                Opt.Str("toName", "The variable receiving the result (default: name)."),
                Opt.Bool("mandatory", "Whether a failed conversion fails the exchange (default true)."),
                Opt.Ignored("charset", OptionKind.String, "strings are converted with UTF-8"),
            ],
            Build = o => new ConvertVariableDefinition(o.RequiredString("name"), o.Type("type")!)
            {
                ToName = o.String("toName"),
                Mandatory = o.Bool("mandatory") ?? true,
            },
        },
        new()
        {
            Name = "removeHeader",
            Description = "Removes a header.",
            InlineOption = "name",
            Options = [Opt.Str("name", "The header name.", required: true).Alias("headerName")],
            Build = o => new RemoveHeaderDefinition(o.RequiredString("name")),
        },
        new()
        {
            Name = "removeHeaders",
            Description = "Removes the headers matching a pattern (exact, wildcard or regular expression).",
            InlineOption = "pattern",
            Options = [Opt.Str("pattern", "The pattern.", required: true), Opt.Strings("excludePattern", "Patterns of headers to keep.")],
            Build = o => new RemoveHeadersDefinition(o.RequiredString("pattern"), [.. o.Strings("excludePattern")]),
        },
        new()
        {
            Name = "removeProperty",
            Description = "Removes an exchange property.",
            InlineOption = "name",
            Options = [Opt.Str("name", "The property name.", required: true).Alias("propertyName")],
            Build = o => new RemovePropertyDefinition(o.RequiredString("name")),
        },
        new()
        {
            Name = "removeProperties",
            Description = "Removes the exchange properties matching a pattern.",
            InlineOption = "pattern",
            Options = [Opt.Str("pattern", "The pattern.", required: true), Opt.Strings("excludePattern", "Patterns of properties to keep.")],
            Build = o => new RemovePropertiesDefinition(o.RequiredString("pattern"), [.. o.Strings("excludePattern")]),
        },
        new()
        {
            Name = "convertBodyTo",
            Description = "Converts the body to a type.",
            InlineOption = "type",
            Options =
            [
                Opt.Type("type", "The type (.NET name; String, Integer, byte[]... are accepted).", required: true),
                Opt.Str("charset", "The charset used for byte/text conversions."),
                Opt.Bool("mandatory", "Fails when the conversion is not possible (default true)."),
            ],
            Build = o => new ConvertBodyDefinition(o.Type("type")!) { Charset = o.String("charset"), Mandatory = o.Bool("mandatory") ?? true },
        },
        new()
        {
            Name = "convertHeaderTo",
            Description = "Converts a header to a type.",
            Options =
            [
                Opt.Str("name", "The header name.", required: true),
                Opt.Type("type", "The type.", required: true),
                Opt.Str("toName", "Stores the result in this header instead."),
                Opt.Bool("mandatory", "Fails when the conversion is not possible (default true)."),
                Opt.Unsupported("charset", "header conversion has no charset"),
            ],
            Build = o => new ConvertHeaderDefinition(o.RequiredString("name"), o.Type("type")!)
            {
                ToName = o.String("toName"),
                Mandatory = o.Bool("mandatory") ?? true,
            },
        },
        new()
        {
            Name = "log",
            Description = "Logs a message (a Simple expression).",
            InlineOption = "message",
            Options =
            [
                Opt.Str("message", "The message (Simple).", required: true),
                Opt.Enum("loggingLevel", "The level.", "TRACE", "DEBUG", "INFO", "WARN", "ERROR", "OFF"),
                Opt.Str("logName", "The logger name (default: the route id)."),
                Opt.Ignored("marker", OptionKind.String, "no log markers"),
                Opt.Unsupported("logger", "use logName"),
            ],
            Build = o =>
            {
                var message = o.RequiredString("message");
                _ = Simple(o, message, "message");
                return new LogDefinition(message)
                {
                    LoggingLevel = o.LogLevel("loggingLevel") ?? Microsoft.Extensions.Logging.LogLevel.Information,
                    LogName = o.String("logName"),
                };
            },
        },
        new()
        {
            Name = "setExchangePattern",
            Description = "Changes the exchange pattern.",
            InlineOption = "pattern",
            Options = [Pattern.Req()],
            Build = o => new SetExchangePatternDefinition(o.Enum<ExchangePattern>("pattern")!.Value),
        },
        new()
        {
            Name = "script",
            Description = "Evaluates an expression for its side effects (the message is not changed).",
            Expression = ExpressionSlot.Expression,
            Build = o => new ScriptDefinition(o.InlineExpression()),
        },
        new()
        {
            Name = "sort",
            Description = "Sorts the items of the body (or of an expression) into a list.",
            Expression = ExpressionSlot.Expression,
            ExpressionRequired = false,
            Options = [Opt.Ref("comparator", "An IComparer<object?>")],
            Build = o => new SortDefinition
            {
                Expression = o.OptionalInlineExpression(),
                Comparer = o.String("comparator") is { } comparator ? RegistryRefs.Comparer(o.Context.Context, comparator) : null,
            },
        },
        new()
        {
            Name = "validate",
            Description = "Fails the exchange with a PredicateValidationException when the predicate does not match.",
            Expression = ExpressionSlot.Predicate,
            Options = [Opt.Unsupported("predicateExceptionFactory", "the exception is always a PredicateValidationException")],
            Build = o => new ValidateDefinition(o.InlinePredicate()),
        },
        new()
        {
            Name = "marshal",
            Description = "Marshals the body with a data format: one key naming the data format, with its options.",
            Options = DataFormatCatalog.Options,
            DynamicOptions = DataFormatCatalog.IsDataFormat,
            Build = o =>
            {
                var (name, dataFormat) = DataFormatCatalog.Resolve(o);
                return new MarshalDefinition { DataFormat = dataFormat, DataFormatName = name };
            },
        },
        new()
        {
            Name = "unmarshal",
            Description = "Unmarshals the body with a data format: one key naming the data format, with its options.",
            Options = [.. DataFormatCatalog.Options, Opt.Bool("allowNullBody", "Allows a null body (else InvalidPayloadException).")],
            DynamicOptions = DataFormatCatalog.IsDataFormat,
            Build = o =>
            {
                var (name, dataFormat) = DataFormatCatalog.Resolve(o);
                return new UnmarshalDefinition { DataFormat = dataFormat, DataFormatName = name, AllowNullBody = o.Bool("allowNullBody") ?? false };
            },
        },
    ];

    /// <summary>A Simple expression of a dynamic string option (compiled now, so errors carry the location).</summary>
    public static IExpression Simple(YamlObject options, string text, string option = "uri")
    {
        try
        {
            return SimpleLanguage.Expression(text);
        }
        catch (Exception e) when (e is Language.Simple.SimpleParseException or ArgumentException)
        {
            throw options.Error(option, $"Invalid simple expression in option '{option}' of '{options.Element}': {e.Message}", e);
        }
    }
}
