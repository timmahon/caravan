using Caravan.Language;

namespace Caravan.Builder;

/// <summary>
/// Terse expression factories for routes, meant for <c>using static Caravan.Builder.Lang;</c>:
/// <code>
/// b.From("direct:orders")
///  .Filter(Simple("${header.amount} &gt; 100"), f =&gt; f
///      .SetHeader("customer", Simple("${body.Customer.Name}"))
///      .SetBody(Simple("Order ${header.orderId} for ${header.customer}"))
///      .To("mock:big"));
/// </code>
/// </summary>
public static class Lang
{
    /// <summary>
    /// A Simple text usable as an expression or a predicate (Camel's <c>simple("...")</c>); see <see cref="SimpleLanguage"/>.
    /// </summary>
    /// <exception cref="Caravan.Language.Simple.SimpleParseException">The text is not a valid expression.</exception>
    public static SimpleBuilder Simple(string text) => new(text);

    /// <summary>A Simple text whose expression value is converted to <paramref name="resultType"/>.</summary>
    /// <exception cref="Caravan.Language.Simple.SimpleParseException">The text is not a valid expression.</exception>
    public static SimpleBuilder Simple(string text, Type resultType) => new(text, resultType);

    /// <summary>A constant value (Camel's <c>constant(...)</c>).</summary>
    public static IExpression Constant(object? value) => Expr.Constant(value);

    /// <summary>The message body (Camel's <c>body()</c>).</summary>
    public static IExpression Body() => Expr.Body();

    /// <summary>The header <paramref name="name"/> (Camel's <c>header(...)</c>).</summary>
    public static IExpression Header(string name) => Expr.Header(name);

    /// <summary>The exchange property <paramref name="name"/> (Camel's <c>exchangeProperty(...)</c>).</summary>
    public static IExpression ExchangeProperty(string name) => Expr.Property(name);

    /// <summary>The variable <paramref name="name"/> (the <c>variable</c> language).</summary>
    /// <param name="name">The variable name, optionally prefixed by a repository id.</param>
    /// <returns>The expression.</returns>
    public static IExpression Variable(string name) => Expr.Variable(name);

    /// <summary>The body tokenized on <paramref name="token"/> (Camel's <c>tokenize(...)</c>), e.g. for <c>split</c>.</summary>
    public static IExpression Tokenize(string token) => Expr.Tokenize(token);

    /// <summary>
    /// An expression in the language <paramref name="language"/>, resolved through the exchange's context
    /// (<see cref="ICaravanContext.ResolveLanguage"/>) on first evaluation (Camel's <c>language(name, text)</c>).
    /// </summary>
    public static IExpression Language(string language, string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(language);
        return new LanguageExpression(language, text);
    }
}
