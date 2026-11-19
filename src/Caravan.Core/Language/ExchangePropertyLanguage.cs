using Caravan.Spi;

namespace Caravan.Language;

/// <summary>The <c>exchangeProperty</c> language: the value of the exchange property named by the text.</summary>
public sealed class ExchangePropertyLanguage : ILanguage
{
    /// <inheritdoc />
    public IExpression CreateExpression(string expression) => Expr.Property(expression.Trim());

    /// <inheritdoc />
    public IPredicate CreatePredicate(string expression) => CreateExpression(expression).ToPredicate();
}
