using System.Globalization;
using Caravan.Dsl.Yaml.Catalog;
using Caravan.Model;
using Microsoft.Extensions.Logging;
using YamlDotNet.RepresentationModel;

namespace Caravan.Dsl.Yaml;

/// <summary>
/// A YAML mapping validated against an <see cref="ObjectSpec"/>: unknown, duplicate, unsupported and missing required options are reported
/// with their line and column when it is created; typed getters convert the values (and report values of the wrong type).
/// </summary>
internal sealed class YamlObject
{
    private static readonly string[] CommonNames = ["id", "description", "disabled"];
    private readonly Dictionary<string, (OptionSpec Option, YamlNode Key, YamlNode Value)> _entries = new(StringComparer.Ordinal);

    /// <summary>Validates <paramref name="node"/> (a mapping, a scalar for the inline option, or null for no options).</summary>
    public YamlObject(LoadContext context, string element, YamlNode node, ObjectSpec spec)
    {
        Context = context;
        Element = element;
        Node = node;
        Spec = spec;
        switch (node)
        {
            case YamlMappingNode mapping:
                foreach (var (key, value) in mapping.Children)
                {
                    Add(key, value);
                }

                break;
            case YamlScalarNode scalar when IsNull(scalar):
                break;
            case YamlScalarNode scalar when spec.InlineOption is { } inline:
                _entries[inline] = (spec.Find(inline)!, scalar, scalar);
                break;
            case YamlScalarNode when spec.Expression != ExpressionSlot.None && spec.Options.Count == 0:
                throw context.Error(node, $"'{element}' must be an object with an expression, e.g. {element}: {{ simple: \"...\" }}");
            default:
                throw context.Error(node, $"'{element}' must be an object" + (spec.InlineOption is null ? string.Empty : $" or a string ({spec.InlineOption})"));
        }

        foreach (var option in spec.Options.Where(o => o.Required && !_entries.ContainsKey(o.Name)))
        {
            throw context.Error(node, $"Missing required option '{option.Name}' in '{element}'");
        }

        if (spec.Expression != ExpressionSlot.None && spec.ExpressionRequired && ExpressionKey is null)
        {
            throw context.Error(
                node,
                $"'{element}' needs {(spec.Expression == ExpressionSlot.Predicate ? "a predicate" : "an expression")}, e.g. {element}: {{ simple: \"${{body}}\" }}");
        }
    }

    /// <summary>The load context.</summary>
    public LoadContext Context { get; }

    /// <summary>The element name used in messages (<c>setHeader</c>, <c>route</c>...).</summary>
    public string Element { get; }

    /// <summary>The node read.</summary>
    public YamlNode Node { get; }

    /// <summary>The spec validated against.</summary>
    public ObjectSpec Spec { get; }

    /// <summary>The key of the inline expression (<c>simple</c>, <c>expression</c>...), or null.</summary>
    public YamlScalarNode? ExpressionKey { get; private set; }

    /// <summary>The value of the inline expression, or null.</summary>
    public YamlNode? ExpressionValue { get; private set; }

    /// <summary>The keys accepted by <see cref="ObjectSpec.DynamicOptions"/>.</summary>
    public List<(string Name, YamlScalarNode Key, YamlNode Value)> DynamicEntries { get; } = [];

    /// <summary>The names of the options given.</summary>
    public IEnumerable<string> Names => _entries.Keys;

    /// <summary>Whether a null or empty scalar (<c>~</c>, <c>null</c>, nothing).</summary>
    public static bool IsNull(YamlNode node) =>
        node is YamlScalarNode { Style: YamlDotNet.Core.ScalarStyle.Plain or YamlDotNet.Core.ScalarStyle.Any } scalar
        && (scalar.Value is null or "" or "~" or "null" or "Null" or "NULL");

    /// <summary>Whether the option is given.</summary>
    public bool Has(string name) => _entries.ContainsKey(Check(name));

    /// <summary>The value node of an option, or null.</summary>
    public YamlNode? Raw(string name) => _entries.TryGetValue(Check(name), out var entry) ? entry.Value : null;

    /// <summary>An error located at the option's value (or the object when the option is absent).</summary>
    public YamlRouteDefinitionException Error(string name, string reason, Exception? innerException = null) =>
        Context.Error(Raw(name) ?? Node, reason, innerException);

    /// <summary>A text option.</summary>
    public string? String(string name) => Raw(name) is { } node ? Scalar(name, node, "a string") : null;

    /// <summary>A required text option (validated as present when created).</summary>
    public string RequiredString(string name) =>
        String(name) is { Length: > 0 } text ? text : throw Error(name, $"Option '{name}' of '{Element}' must not be empty");

    /// <summary>A boolean option.</summary>
    public bool? Bool(string name) => Typed(name, "a boolean (true or false)", text => text.ToLowerInvariant() switch
    {
        "true" => true,
        "false" => false,
        _ => (bool?)null,
    });

    /// <summary>An integer option.</summary>
    public int? Int(string name) =>
        Typed(name, "an integer", text => int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : (int?)null);

    /// <summary>A long integer option.</summary>
    public long? Long(string name) =>
        Typed(name, "an integer", text => long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : (long?)null);

    /// <summary>A decimal number option.</summary>
    public double? Double(string name) =>
        Typed(name, "a number", text => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : (double?)null);

    /// <summary>A duration option.</summary>
    public TimeSpan? Duration(string name) =>
        Typed(name, "a duration (milliseconds, or e.g. 5s, 1m30s, PT5S)", text => DurationParser.TryParse(text, out var value) ? value : (TimeSpan?)null);

    /// <summary>An enumeration option (case-insensitive; Camel's upper-case names with underscores are accepted).</summary>
    public T? Enum<T>(string name)
        where T : struct, Enum =>
        Typed(name, "one of " + string.Join(", ", Find(name).EnumValues ?? System.Enum.GetNames<T>()), text =>
            System.Enum.TryParse<T>(text.Replace("_", string.Empty, StringComparison.Ordinal), ignoreCase: true, out var value) && System.Enum.IsDefined(value)
                ? value
                : (T?)null);

    /// <summary>A Camel logging level option (TRACE, DEBUG, INFO, WARN, ERROR, OFF).</summary>
    public LogLevel? LogLevel(string name) => Typed(name, "a logging level (TRACE, DEBUG, INFO, WARN, ERROR, OFF)", text => text.ToUpperInvariant() switch
    {
        "TRACE" => Microsoft.Extensions.Logging.LogLevel.Trace,
        "DEBUG" => Microsoft.Extensions.Logging.LogLevel.Debug,
        "INFO" or "INFORMATION" => Microsoft.Extensions.Logging.LogLevel.Information,
        "WARN" or "WARNING" => Microsoft.Extensions.Logging.LogLevel.Warning,
        "ERROR" => Microsoft.Extensions.Logging.LogLevel.Error,
        "CRITICAL" => Microsoft.Extensions.Logging.LogLevel.Critical,
        "OFF" or "NONE" => Microsoft.Extensions.Logging.LogLevel.None,
        _ => (LogLevel?)null,
    });

    /// <summary>A type option.</summary>
    public Type? Type(string name) => Raw(name) is { } node ? ResolveType(name, node) : null;

    /// <summary>A list of types (a single name is accepted).</summary>
    public List<Type> Types(string name) => Items(name).Select(node => ResolveType(name, node)).ToList();

    /// <summary>A list of strings (a single string is accepted).</summary>
    public List<string> Strings(string name) => Items(name).Select(node => Scalar(name, node, "a string") ?? string.Empty).ToList();

    /// <summary>A nested expression option (<c>correlationExpression: { simple: "..." }</c>).</summary>
    public IExpression? Expression(string name) =>
        Raw(name) is { } node ? ExpressionParser.ParseExpressionHolder(Context, $"{Element}.{name}", node) : null;

    /// <summary>A nested predicate option (<c>onWhen: { simple: "..." }</c>; <c>true</c>/<c>false</c> are accepted).</summary>
    public IPredicate? Predicate(string name) =>
        Raw(name) is { } node ? ExpressionParser.ParsePredicateHolder(Context, $"{Element}.{name}", node) : null;

    /// <summary>The inline expression (validated as present when required).</summary>
    public IExpression InlineExpression() =>
        ExpressionKey is null
            ? throw Context.Error(Node, $"'{Element}' needs an expression")
            : ExpressionParser.ParseExpression(Context, Element, ExpressionKey, ExpressionValue!);

    /// <summary>The inline expression, or null when absent.</summary>
    public IExpression? OptionalInlineExpression() => ExpressionKey is null ? null : InlineExpression();

    /// <summary>The inline predicate (validated as present when required).</summary>
    public IPredicate InlinePredicate() =>
        ExpressionKey is null
            ? throw Context.Error(Node, $"'{Element}' needs a predicate")
            : ExpressionParser.ParsePredicate(Context, Element, ExpressionKey, ExpressionValue!);

    /// <summary>Reads a list of steps into <paramref name="outputs"/>.</summary>
    public void Steps(string name, List<ProcessorDefinition> outputs, RouteDefinition? route = null)
    {
        if (Raw(name) is { } node)
        {
            StepParser.ParseSteps(Context, node, outputs, route);
        }
    }

    /// <summary>A nested object option, or null.</summary>
    public YamlObject? Object(string name) =>
        Raw(name) is { } node ? new YamlObject(Context, name, node, Find(name).Nested!) : null;

    /// <summary>The items of a list-of-objects option (a single object is accepted; wrapped items are unwrapped).</summary>
    public List<YamlObject> Objects(string name)
    {
        var option = Find(name);
        return Items(name).Select(node =>
        {
            if (option.ItemWrapper is { } wrapper && node is YamlMappingNode { Children.Count: 1 } single
                && single.Children.First().Key is YamlScalarNode { Value: { } key } && NameConverter.ToCamelCase(key) == wrapper)
            {
                node = single.Children.First().Value;
            }

            return new YamlObject(Context, option.ItemWrapper ?? name, node, option.Nested!);
        }).ToList();
    }

    /// <summary>The entries of a free-form mapping option.</summary>
    public List<(string Key, YamlNode KeyNode, YamlNode Value)> Map(string name)
    {
        if (Raw(name) is not { } node || IsNull(node))
        {
            return [];
        }

        if (node is not YamlMappingNode mapping)
        {
            throw Error(name, $"Option '{name}' of '{Element}' must be a mapping");
        }

        return mapping.Children.Select(e => (
            e.Key is YamlScalarNode { Value: { } key } ? key : throw Context.Error(e.Key, "Keys must be strings"),
            e.Key,
            e.Value)).ToList();
    }

    /// <summary>Applies the common step options (<c>id</c>, <c>description</c>, <c>disabled</c>) to <paramref name="definition"/>.</summary>
    public void ApplyCommon(ProcessorDefinition definition)
    {
        if (String("id") is { } id)
        {
            definition.Id = id;
        }

        if (String("description") is { } description)
        {
            definition.Description = description;
        }

        if (Bool("disabled") is { } disabled)
        {
            definition.Disabled = disabled;
        }
    }

    /// <summary>The text of a scalar value: placeholders are kept (they are resolved where the value is used).</summary>
    public string? Scalar(string name, YamlNode node, string expected) => node switch
    {
        YamlScalarNode scalar when IsNull(scalar) => null,
        YamlScalarNode scalar => Context.Text(scalar.Value ?? string.Empty),
        _ => throw Context.Error(node, $"Option '{name}' of '{Element}' must be {expected}"),
    };

    private T? Typed<T>(string name, string expected, Func<string, T?> parse)
        where T : struct
    {
        if (Raw(name) is not { } node || Scalar(name, node, expected) is not { } text)
        {
            return null;
        }

        text = text.Trim();
        if (text.Contains("{{", StringComparison.Ordinal))
        {
            if (Context.InTemplate)
            {
                // Read again with the parameters substituted for each route created from the template.
                Context.TemplateNeedsRebuild = true;
                return default(T);
            }

            try
            {
                text = Context.Context.ResolvePropertyPlaceholders(text).Trim();
            }
            catch (Exception e) when (e is ArgumentException or InvalidOperationException)
            {
                throw Context.Error(node, $"Option '{name}' of '{Element}': {e.Message}", e);
            }
        }

        return parse(text) ?? throw Context.Error(node, $"Option '{name}' of '{Element}' must be {expected} but was '{text}'");
    }

    private Type ResolveType(string name, YamlNode node)
    {
        var text = Scalar(name, node, "a type name") ?? throw Context.Error(node, $"Option '{name}' of '{Element}' must be a type name");
        return TypeResolver.Resolve(Context.Context, text) ?? throw Context.Error(node, $"Cannot resolve type '{text}' (option '{name}' of '{Element}')");
    }

    private IEnumerable<YamlNode> Items(string name) => Raw(name) switch
    {
        null => [],
        YamlSequenceNode sequence => sequence.Children,
        YamlScalarNode scalar when IsNull(scalar) => [],
        var node => [node],
    };

    private OptionSpec Find(string name) =>
        Spec.Find(name) ?? (Spec.CommonOptions ? CommonOption(name) : null)
        ?? throw new InvalidOperationException($"Catalog error: '{Element}' has no option '{name}'");

    private string Check(string name)
    {
        _ = Find(name);
        return name;
    }

    private static OptionSpec? CommonOption(string name) => name switch
    {
        "id" => Opt.Id,
        "description" => Opt.Description,
        "disabled" => Opt.Disabled,
        _ => null,
    };

    private void Add(YamlNode keyNode, YamlNode value)
    {
        if (keyNode is not YamlScalarNode { Value: { Length: > 0 } rawKey } key)
        {
            throw Context.Error(keyNode, $"Keys of '{Element}' must be strings");
        }

        var name = NameConverter.ToCamelCase(rawKey);
        var option = Spec.Find(name) ?? (Spec.CommonOptions ? CommonOption(name) : null);
        if (option is not null)
        {
            if (option.Unsupported is { } why)
            {
                throw Context.Error(keyNode, $"Option '{rawKey}' of '{Element}' is not supported by Caravan: {why}");
            }

            if (!_entries.TryAdd(option.Name, (option, keyNode, value)))
            {
                throw Context.Error(keyNode, $"Duplicate option '{rawKey}' in '{Element}'");
            }

            return;
        }

        if (Spec.DynamicOptions?.Invoke(Context.Context, name) == true)
        {
            if (DynamicEntries.Any(e => e.Name == name))
            {
                throw Context.Error(keyNode, $"Duplicate option '{rawKey}' in '{Element}'");
            }

            DynamicEntries.Add((name, key, value));
            return;
        }

        if (Spec.Expression != ExpressionSlot.None && (name == "expression" || LanguageCatalog.IsLanguage(Context.Context, name)))
        {
            if (ExpressionKey is not null)
            {
                throw Context.Error(keyNode, $"'{Element}' has more than one expression ('{ExpressionKey.Value}' and '{rawKey}')");
            }

            ExpressionKey = key;
            ExpressionValue = value;
            return;
        }

        var known = Spec.Options.Where(o => o.Unsupported is null).Select(o => o.Name).Concat(Spec.CommonOptions ? CommonNames : []).ToList();
        var hint = NameConverter.Closest(name, known) is { } closest ? $" Did you mean '{closest}'?" : string.Empty;
        var languages = Spec.Expression != ExpressionSlot.None ? " (or an expression language such as simple, constant, header)" : string.Empty;
        throw Context.Error(
            keyNode,
            $"Unknown option '{rawKey}' in '{Element}'.{hint} Valid options: {(known.Count == 0 ? "none" : string.Join(", ", known))}{languages}");
    }
}
