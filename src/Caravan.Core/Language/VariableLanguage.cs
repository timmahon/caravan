using Caravan.Spi;

namespace Caravan.Language;

/// <summary>The <c>variable</c> language (Camel 4): the value of the variable named by the text (<c>global:</c>/<c>route:</c> prefixes allowed).</summary>
public sealed class VariableLanguage : ILanguage
{
    /// <inheritdoc />
    public IExpression CreateExpression(string expression) => Expr.Variable(expression.Trim());

    /// <inheritdoc />
    public IPredicate CreatePredicate(string expression) => CreateExpression(expression).ToPredicate();
}
