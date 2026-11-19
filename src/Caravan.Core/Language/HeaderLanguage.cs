using Caravan.Spi;

namespace Caravan.Language;

/// <summary>The <c>header</c> language: the value of the message header named by the text.</summary>
public sealed class HeaderLanguage : ILanguage
{
    /// <inheritdoc />
    public IExpression CreateExpression(string expression) => Expr.Header(expression.Trim());

    /// <inheritdoc />
    public IPredicate CreatePredicate(string expression) => CreateExpression(expression).ToPredicate();
}
