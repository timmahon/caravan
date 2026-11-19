using Caravan.Spi;

namespace Caravan.Language;

/// <summary>
/// An expression (and predicate) in a language named by text, as written in text DSLs (YAML): the language is resolved from the exchange's
/// context (<see cref="ICaravanContext.ResolveLanguage"/>) and the expression or predicate created on first use, then reused. Like Camel at
/// reification, <c>{{placeholders}}</c> in the text are resolved with the properties component first (except for <c>simple</c>, which resolves
/// them itself). Route templates substitute their parameters into <see cref="Text"/> (see <see cref="Model.RouteDefinition.Clone"/>), so a
/// template's <c>constant: "{{greeting}}"</c> works as in Camel.
/// </summary>
public sealed class LanguageExpression : IExpression, IPredicate
{
    private readonly object _lock = new();
    private IExpression? _expression;
    private IPredicate? _predicate;

    /// <summary>Creates the expression.</summary>
    /// <param name="language">The language name, e.g. <c>constant</c>, <c>header</c>, <c>method</c>.</param>
    /// <param name="text">The expression text in that language.</param>
    /// <param name="resultType">When set, the value is converted to this type with the context's type converter.</param>
    public LanguageExpression(string language, string text, Type? resultType = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(language);
        ArgumentNullException.ThrowIfNull(text);
        Language = language;
        Text = text;
        ResultType = resultType;
    }

    /// <summary>The language name.</summary>
    public string Language { get; }

    /// <summary>The expression text.</summary>
    public string Text { get; }

    /// <summary>The type the value is converted to, or null.</summary>
    public Type? ResultType { get; }

    /// <summary>A copy with another text (same language and result type).</summary>
    public LanguageExpression WithText(string text) => new(Language, text, ResultType);

    /// <inheritdoc />
    public object? Evaluate(Exchange exchange)
    {
        ArgumentNullException.ThrowIfNull(exchange);
        var expression = _expression;
        if (expression is null)
        {
            lock (_lock)
            {
                expression = _expression ??= Resolve(exchange.Context).CreateExpression(ResolveText(exchange.Context));
            }
        }

        var value = expression.Evaluate(exchange);
        return ResultType is null || value is null || ResultType.IsInstanceOfType(value)
            ? value
            : exchange.Context.TypeConverter.Convert(ResultType, exchange, value);
    }

    /// <inheritdoc />
    public bool Matches(Exchange exchange)
    {
        ArgumentNullException.ThrowIfNull(exchange);
        var predicate = _predicate;
        if (predicate is null)
        {
            lock (_lock)
            {
                predicate = _predicate ??= Resolve(exchange.Context).CreatePredicate(ResolveText(exchange.Context));
            }
        }

        return predicate.Matches(exchange);
    }

    /// <inheritdoc />
    public override string ToString() => $"{Language}{{{Text}}}";

    private ILanguage Resolve(ICaravanContext context) => context.ResolveLanguage(Language);

    private string ResolveText(ICaravanContext context) =>
        !string.Equals(Language, "simple", StringComparison.OrdinalIgnoreCase) && Text.Contains("{{", StringComparison.Ordinal)
            ? context.ResolvePropertyPlaceholders(Text)
            : Text;
}
