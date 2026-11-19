namespace Caravan;

/// <summary>Helpers for evaluating and adapting expressions.</summary>
public static class ExpressionExtensions
{
    /// <summary>Evaluates the expression and converts the result to <typeparamref name="T"/>.</summary>
    public static T? Evaluate<T>(this IExpression expression, Exchange exchange) =>
        (T?)exchange.Context.TypeConverter.Convert(typeof(T), exchange, expression.Evaluate(exchange));

    /// <summary>Uses an expression as a predicate: true when the result converts to <c>true</c>, or is a non-null, non-boolean value.</summary>
    public static IPredicate ToPredicate(this IExpression expression) => new ExpressionPredicate(expression);

    private sealed class ExpressionPredicate(IExpression expression) : IPredicate
    {
        public bool Matches(Exchange exchange) => PredicateHelper.ToBoolean(exchange, expression.Evaluate(exchange));

        public override string ToString() => expression.ToString() ?? "expression";
    }
}
