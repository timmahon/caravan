namespace Caravan.Dsl.Yaml.Catalog;

/// <summary>
/// The expression languages of the YAML DSL and the options of their long form (<c>simple: { expression: "...", resultType: int }</c>).
/// Any other language known to the context (<see cref="ICaravanContext.Languages"/>, an <see cref="Spi.ILanguage"/> bean) is accepted with the
/// generic options.
/// </summary>
internal static class LanguageCatalog
{
    /// <summary>The options of a language without specific options.</summary>
    public static readonly ObjectSpec Generic = new()
    {
        InlineOption = "expression",
        Options =
        [
            Opt.Str("expression", "The expression text.", required: true),
            Opt.Type("resultType", "The type the value is converted to."),
            Opt.Bool("trim", "Whether the text is trimmed (default true)."),
            Opt.Ignored("id", OptionKind.String, "expressions have no id"),
        ],
    };

    /// <summary>The built-in languages with their long-form options.</summary>
    public static readonly IReadOnlyDictionary<string, (string Description, ObjectSpec Spec)> Languages =
        new Dictionary<string, (string, ObjectSpec)>(StringComparer.Ordinal)
        {
            ["simple"] = ("The Simple language: ${body}, ${header.x}, operators and functions.", Generic),
            ["constant"] = ("A constant value (converted to resultType when given).", Generic),
            ["header"] = ("The value of the named header.", Generic),
            ["exchangeProperty"] = ("The value of the named exchange property.", Generic),
            ["ref"] = ("A registry bean that is an expression or predicate.", Generic),
            ["tokenize"] = ("The body split on a token.", new ObjectSpec
            {
                InlineOption = "token",
                Options =
                [
                    Opt.Str("token", "The token (or regular expression with regex: true).", required: true),
                    Opt.Bool("regex", "Whether the token is a regular expression."),
                    Opt.Int("group", "Groups this many tokens into one part (joined with newlines)."),
                    Opt.Bool("skipFirst", "Skips the first token (e.g. a header line)."),
                    Opt.Bool("trim", "Whether the token is trimmed (default true)."),
                    Opt.Ignored("id", OptionKind.String, "expressions have no id"),
                    Opt.Unsupported("endToken", "tokenizing between start and end tokens is not supported"),
                    Opt.Unsupported("inheritNamespaceTagName", "XML tokenizing is not supported"),
                    Opt.Unsupported("xml", "XML tokenizing is not supported"),
                    Opt.Unsupported("includeTokens", "including the tokens is not supported"),
                    Opt.Unsupported("groupDelimiter", "groups are joined with newlines"),
                    Opt.Unsupported("source", "tokenize reads the body"),
                ],
            }),
            ["method"] = ("The result of a bean method (the bean language).", new ObjectSpec
            {
                InlineOption = "ref",
                Options =
                [
                    Opt.Ref("ref", "The bean"),
                    Opt.Str("beanType", "The type of the bean (instantiated, or its static methods called)."),
                    Opt.Str("method", "The method name (or signature)."),
                    Opt.Enum("scope", "The bean scope.", "Singleton", "Request", "Prototype"),
                    Opt.Ignored("validate", OptionKind.Boolean, "methods are validated when the route is created"),
                    Opt.Ignored("id", OptionKind.String, "expressions have no id"),
                ],
            }),
            ["language"] = ("An expression in a language named by the language option.", new ObjectSpec
            {
                Options =
                [
                    Opt.Str("language", "The language name.", required: true),
                    Opt.Str("expression", "The expression text.", required: true),
                    Opt.Type("resultType", "The type the value is converted to."),
                    Opt.Bool("trim", "Whether the text is trimmed (default true)."),
                    Opt.Ignored("id", OptionKind.String, "expressions have no id"),
                ],
            }),
        };

    /// <summary>Whether <paramref name="name"/> is an expression language (built-in or known to the context).</summary>
    public static bool IsLanguage(ICaravanContext context, string name) =>
        Languages.ContainsKey(name)
        || context.Languages.Names.Contains(name, StringComparer.OrdinalIgnoreCase)
        || context.Registry.Lookup(name) is Spi.ILanguage;

    /// <summary>The long-form options of <paramref name="name"/>.</summary>
    public static ObjectSpec SpecOf(string name) => Languages.TryGetValue(name, out var language) ? language.Spec : Generic;
}
