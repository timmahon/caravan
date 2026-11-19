using Caravan.Spi;

namespace Caravan.Language;

/// <summary>
/// The <c>bean</c> language (also registered as <c>method</c>, Camel's <c>method</c> language): the text names a bean and a method, see
/// <see cref="BeanExpression.Parse"/>; the expression's value is the method's result, a predicate converts it with
/// <see cref="PredicateHelper.ToBoolean"/>. It also provides the Simple function <c>${bean:name?method=foo}</c> / <c>${bean:name.foo}</c>.
/// </summary>
public sealed class BeanLanguage : ILanguage
{
    /// <inheritdoc />
    public IExpression CreateExpression(string expression) => BeanExpression.Parse(expression);

    /// <inheritdoc />
    public IPredicate CreatePredicate(string expression)
    {
        var bean = BeanExpression.Parse(expression);
        return new FuncPredicate(e => PredicateHelper.ToBoolean(e, bean.Evaluate(e)), bean.ToString());
    }
}
