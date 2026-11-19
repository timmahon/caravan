namespace Caravan.Language;

/// <summary>
/// A Simple text usable both as an expression and as a predicate, like Camel's <c>simple("...")</c> in the Java DSL; created by
/// <see cref="Builder.Lang.Simple(string)"/>. The expression form is compiled at once; the predicate form when the route is created
/// (or on first use), so a malformed predicate fails the route's creation with a <see cref="Simple.SimpleParseException"/>.
/// </summary>
public sealed class SimpleBuilder : IExpression, IPredicate
{
    private readonly SimpleExpression _expression;
    private SimplePredicate? _predicate;

    /// <summary>Creates the builder for <paramref name="text"/>.</summary>
    /// <param name="text">The Simple text.</param>
    /// <param name="resultType">The type the expression value is converted to; null keeps it as is.</param>
    /// <exception cref="Simple.SimpleParseException">The text is not a valid expression.</exception>
    public SimpleBuilder(string text, Type? resultType = null)
    {
        _expression = SimpleLanguage.Expression(text, resultType);
    }

    /// <summary>The Simple text.</summary>
    public string Text => _expression.Text;

    /// <summary>The type the expression value is converted to.</summary>
    public Type? ResultType => _expression.ResultType;

    /// <inheritdoc />
    public object? Evaluate(Exchange exchange) => _expression.Evaluate(exchange);

    /// <inheritdoc />
    public bool Matches(Exchange exchange) => AsPredicate().Matches(exchange);

    /// <summary>The predicate form, compiled on first use.</summary>
    /// <exception cref="Simple.SimpleParseException">The text is not a valid predicate.</exception>
    public SimplePredicate AsPredicate() => _predicate ??= SimpleLanguage.Predicate(Text);

    /// <inheritdoc />
    public override string ToString() => _expression.ToString();
}
