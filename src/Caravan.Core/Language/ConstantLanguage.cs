using Caravan.Spi;

namespace Caravan.Language;

/// <summary>The <c>constant</c> language: the text itself (a predicate is true for <c>true</c> or any other non-empty text but <c>false</c>).</summary>
public sealed class ConstantLanguage : ILanguage
{
    /// <inheritdoc />
    public IExpression CreateExpression(string expression) => Expr.Constant(expression);

    /// <inheritdoc />
    public IPredicate CreatePredicate(string expression) => Expr.Constant(expression).ToPredicate();
}
