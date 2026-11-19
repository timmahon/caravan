using Caravan.Spi;

namespace Caravan.Language;

/// <summary>
/// The <c>ref</c> language: the text names a bean in the registry, looked up when evaluated, that is an <see cref="IExpression"/>
/// or an <see cref="IPredicate"/> (an expression used as a predicate follows <see cref="PredicateHelper.ToBoolean"/>).
/// </summary>
public sealed class RefLanguage : ILanguage
{
    /// <inheritdoc />
    public IExpression CreateExpression(string expression)
    {
        var name = expression.Trim();
        return new FuncExpression(
            e => Lookup(e, name) switch
            {
                IExpression x => x.Evaluate(e),
                IPredicate p => p.Matches(e),
                var other => throw new InvalidOperationException($"Bean '{name}' is a {other.GetType().Name}, not an expression"),
            },
            $"ref:{name}");
    }

    /// <inheritdoc />
    public IPredicate CreatePredicate(string expression)
    {
        var name = expression.Trim();
        return new FuncPredicate(
            e => Lookup(e, name) switch
            {
                IPredicate p => p.Matches(e),
                IExpression x => PredicateHelper.ToBoolean(e, x.Evaluate(e)),
                var other => throw new InvalidOperationException($"Bean '{name}' is a {other.GetType().Name}, not a predicate"),
            },
            $"ref:{name}");
    }

    private static object Lookup(Exchange exchange, string name) =>
        exchange.Context.Registry.Lookup(name) ?? throw new NoSuchBeanException(name);
}
