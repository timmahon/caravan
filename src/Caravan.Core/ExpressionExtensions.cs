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

    /// <summary>Tokenizes the value of this expression (Camel's <c>body().tokenize(",")</c>); see <see cref="Expr.Tokenize(string, bool, int, bool)"/>.</summary>
    /// <param name="expression">The text to tokenize.</param>
    /// <param name="token">The separator.</param>
    /// <param name="regex">Whether <paramref name="token"/> is a regular expression.</param>
    /// <param name="group">When above 1, joins that many parts into one value.</param>
    /// <param name="skipFirst">Skips the first part.</param>
    public static IExpression Tokenize(this IExpression expression, string token, bool regex = false, int group = 0, bool skipFirst = false) =>
        Expr.Tokenize(expression, token, regex, group, skipFirst);

    /// <summary>The lines of the value of this expression; see <see cref="Expr.Lines(int, bool)"/>.</summary>
    /// <param name="expression">The text to split in lines.</param>
    /// <param name="group">When above 1, joins that many lines into one value.</param>
    /// <param name="skipFirst">Skips the first line.</param>
    public static IExpression TokenizeLines(this IExpression expression, int group = 0, bool skipFirst = false) =>
        Expr.Lines(expression, group, skipFirst);

    private sealed class ExpressionPredicate(IExpression expression) : IPredicate
    {
        public bool Matches(Exchange exchange) => PredicateHelper.ToBoolean(exchange, expression.Evaluate(exchange));

        public override string ToString() => expression.ToString() ?? "expression";
    }
}
