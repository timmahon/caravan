namespace Caravan;

/// <summary>Helpers for evaluating and adapting expressions.</summary>
public static class ExpressionExtensions
{
    /// <summary>Evaluates the expression and converts the result to <typeparamref name="T"/>.</summary>
    public static T? Evaluate<T>(this IExpression expression, Exchange exchange) =>
        (T?)exchange.Context.TypeConverter.Convert(typeof(T), exchange, expression.Evaluate(exchange));

    /// <summary>Uses an expression as a predicate: true when the result converts to <c>true</c>, or is a non-null, non-boolean value.</summary>
    public static IPredicate ToPredicate(this IExpression expression) => new ExpressionPredicate(expression);

    /// <summary>Appends <paramref name="suffix"/> (a value or an expression) to the string value of this expression.</summary>
    public static IExpression Append(this IExpression expression, object? suffix) => Expr.Append(expression, suffix);

    /// <summary>Prepends <paramref name="prefix"/> (a value or an expression) to the string value of this expression.</summary>
    public static IExpression Prepend(this IExpression expression, object? prefix) => Expr.Prepend(expression, prefix);

    /// <summary>Converts the value of this expression to <typeparamref name="T"/>; see <see cref="Expr.Convert{T}"/>.</summary>
    public static IExpression ConvertTo<T>(this IExpression expression) => Expr.Convert<T>(expression);

    /// <summary>Replaces every match of <paramref name="regex"/> in the string value; see <see cref="Expr.RegexReplaceAll"/>.</summary>
    public static IExpression RegexReplaceAll(this IExpression expression, string regex, object? replacement) =>
        Expr.RegexReplaceAll(expression, regex, replacement);

    private sealed class ExpressionPredicate(IExpression expression) : IPredicate
    {
        public bool Matches(Exchange exchange) => PredicateHelper.ToBoolean(exchange, expression.Evaluate(exchange));

        public override string ToString() => expression.ToString() ?? "expression";
    }
}
