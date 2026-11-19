using Caravan.Spi;

namespace Caravan.Language;

/// <summary>The <c>tokenize</c> language: the text is the separator the body is tokenized on (see <see cref="Expr.Tokenize(string, bool, int, bool)"/>).</summary>
public sealed class TokenizeLanguage : ILanguage
{
    /// <inheritdoc />
    public IExpression CreateExpression(string expression) => Expr.Tokenize(expression);

    /// <inheritdoc />
    public IPredicate CreatePredicate(string expression) => CreateExpression(expression).ToPredicate();
}
