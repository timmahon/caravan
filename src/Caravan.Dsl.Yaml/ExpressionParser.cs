using Caravan.Dsl.Yaml.Catalog;
using Caravan.Language;
using YamlDotNet.RepresentationModel;

namespace Caravan.Dsl.Yaml;

/// <summary>
/// Turns Camel's YAML expressions into Caravan expressions and predicates, resolving the language by name: short form
/// (<c>simple: "${body}"</c>), long form (<c>simple: { expression: "...", resultType: int }</c>), wrapped
/// (<c>expression: { simple: ... }</c>) and generic (<c>language: { language: x, expression: y }</c>). Simple texts become Simple expressions;
/// other languages become <see cref="LanguageExpression"/>s (resolved through the context's language registry, with route template parameters
/// substituted per route). Texts without placeholders are compiled once while loading so that errors carry their line and column.
/// </summary>
internal static class ExpressionParser
{
    /// <summary>An object holding one expression: <c>{ simple: ... }</c> or <c>{ expression: { simple: ... } }</c>.</summary>
    public static IExpression ParseExpressionHolder(LoadContext context, string element, YamlNode node)
    {
        var (key, value) = Single(context, element, node);
        return ParseExpression(context, element, key, value);
    }

    /// <summary>An object holding one predicate, or a boolean scalar.</summary>
    public static IPredicate ParsePredicateHolder(LoadContext context, string element, YamlNode node)
    {
        if (node is YamlScalarNode { Value: { } text } && bool.TryParse(context.Text(text).Trim(), out var constant))
        {
            return Pred.Constant(constant);
        }

        var (key, value) = Single(context, element, node);
        return ParsePredicate(context, element, key, value);
    }

    /// <summary>The expression of the language entry <paramref name="key"/>: <paramref name="value"/>.</summary>
    public static IExpression ParseExpression(LoadContext context, string element, YamlScalarNode key, YamlNode value) =>
        (IExpression)Parse(context, element, key, value, predicate: false);

    /// <summary>The predicate of the language entry <paramref name="key"/>: <paramref name="value"/>.</summary>
    public static IPredicate ParsePredicate(LoadContext context, string element, YamlScalarNode key, YamlNode value) =>
        (IPredicate)Parse(context, element, key, value, predicate: true);

    private static (YamlScalarNode Key, YamlNode Value) Single(LoadContext context, string element, YamlNode node)
    {
        if (node is not YamlMappingNode { Children.Count: 1 } mapping || mapping.Children.First().Key is not YamlScalarNode key)
        {
            throw context.Error(node, $"'{element}' must be an object with one expression, e.g. {{ simple: \"${{body}}\" }}");
        }

        return (key, mapping.Children.First().Value);
    }

    private static object Parse(LoadContext context, string element, YamlScalarNode key, YamlNode value, bool predicate)
    {
        var language = NameConverter.ToCamelCase(key.Value ?? string.Empty);
        if (language == "expression")
        {
            var (innerKey, innerValue) = Single(context, element, value);
            return Parse(context, element, innerKey, innerValue, predicate);
        }

        if (!LanguageCatalog.IsLanguage(context.Context, language))
        {
            var hint = NameConverter.Closest(language, LanguageCatalog.Languages.Keys) is { } closest ? $" Did you mean '{closest}'?" : string.Empty;
            throw context.Error(key, $"Unknown expression language '{key.Value}' in '{element}'.{hint}");
        }

        var options = new YamlObject(context, language, value, LanguageCatalog.SpecOf(language));
        switch (language)
        {
            case "language":
                return Create(context, key, options.RequiredString("language"), Text(options, "expression"), options.Type("resultType"), predicate);
            case "tokenize":
                {
                    var token = options.RequiredString("token");
                    var regex = options.Bool("regex") ?? false;
                    var group = options.Int("group") ?? 0;
                    var skipFirst = options.Bool("skipFirst") ?? false;
                    if (!regex && group == 0 && !skipFirst)
                    {
                        return Create(context, key, "tokenize", token, null, predicate);
                    }

                    try
                    {
                        return AsResult(Expr.Tokenize(token, regex, group, skipFirst), predicate);
                    }
                    catch (ArgumentException e)
                    {
                        throw context.Error(value, $"Invalid tokenize expression in '{element}': {e.Message}", e);
                    }
                }

            case "method":
                {
                    var bean = options.String("ref");
                    var beanType = options.String("beanType");
                    if ((bean is null) == (beanType is null))
                    {
                        throw context.Error(value, $"The method expression in '{element}' needs either ref or beanType");
                    }

                    var text = bean ?? "class:" + beanType;
                    var parameters = new List<string>();
                    if (options.String("method") is { } method)
                    {
                        parameters.Add("method=" + method);
                    }

                    if (options.Enum<BeanScope>("scope") is { } scope)
                    {
                        parameters.Add("scope=" + scope);
                    }

                    return Create(context, key, "method", parameters.Count == 0 ? text : text + "?" + string.Join("&", parameters), null, predicate);
                }

            default:
                return Create(context, key, language, Text(options, "expression"), options.Type("resultType"), predicate);
        }
    }

    private static string Text(YamlObject options, string name)
    {
        var text = options.String(name) ?? string.Empty;
        return options.Bool("trim") == false ? text : text.Trim();
    }

    private static object Create(LoadContext context, YamlNode key, string language, string text, Type? resultType, bool predicate)
    {
        if (string.Equals(language, "simple", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                return predicate ? SimpleLanguage.Predicate(text) : new SimpleBuilder(text, resultType);
            }
            catch (Exception e) when (e is Language.Simple.SimpleParseException or ArgumentException)
            {
                throw context.Error(key, $"Invalid simple expression: {e.Message}", e);
            }
        }

        Spi.ILanguage resolved;
        try
        {
            resolved = context.Context.ResolveLanguage(language);
        }
        catch (NoSuchLanguageException e)
        {
            throw context.Error(key, $"Unknown expression language '{language}'", e);
        }

        if (!text.Contains("{{", StringComparison.Ordinal))
        {
            try
            {
                _ = predicate ? (object)resolved.CreatePredicate(text) : resolved.CreateExpression(text);
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                throw context.Error(key, $"Invalid {language} expression '{text}': {e.Message}", e);
            }
        }

        return new LanguageExpression(language, text, resultType);
    }

    private static object AsResult(IExpression expression, bool predicate) =>
        predicate ? new FuncPredicate(e => PredicateHelper.ToBoolean(e, expression.Evaluate(e)), expression.ToString() ?? "tokenize") : expression;
}
