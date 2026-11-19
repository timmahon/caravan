namespace Caravan.Spi;

/// <summary>
/// An expression language (Camel's <c>Language</c>): turns expression text into expressions and predicates. Languages are resolved
/// by name with <see cref="ICaravanContext.ResolveLanguage"/> (built-ins: <c>simple</c>, <c>constant</c>, <c>header</c>,
/// <c>exchangeProperty</c>, <c>tokenize</c>, <c>ref</c>), e.g. by DSLs that read routes from configuration files.
/// Implementations must be thread-safe; the expressions they create are evaluated concurrently.
/// </summary>
public interface ILanguage
{
    /// <summary>Creates an expression from <paramref name="expression"/>.</summary>
    /// <exception cref="ArgumentException">The text is not valid in this language.</exception>
    IExpression CreateExpression(string expression);

    /// <summary>Creates a predicate from <paramref name="expression"/>.</summary>
    /// <exception cref="ArgumentException">The text is not valid in this language.</exception>
    IPredicate CreatePredicate(string expression);
}
