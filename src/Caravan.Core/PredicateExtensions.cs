using System.Text.RegularExpressions;
using Caravan.Support;

namespace Caravan;

/// <summary>
/// Builds predicates from expressions (Camel's <c>PredicateBuilder</c>/<c>ValueBuilder</c>), e.g.
/// <c>Expr.Header("amount").IsGreaterThan(100).And(Expr.Body().Contains("vip"))</c>.
/// Right-hand values may be constants or <see cref="IExpression"/>s. Comparisons convert types like Camel:
/// <c>Expr.Header("n").IsEqualTo(5)</c> matches the header string <c>"5"</c>.
/// </summary>
public static class PredicateExtensions
{
    /// <summary>Equal after type coercion; null equals only null.</summary>
    public static IPredicate IsEqualTo(this IExpression left, object? right) =>
        Binary(left, right, "==", (e, l, r) => ObjectHelper.TypeCoerceEquals(e, l, r));

    /// <summary>Equal after type coercion, ignoring case for strings.</summary>
    public static IPredicate IsEqualToIgnoreCase(this IExpression left, object? right) =>
        Binary(left, right, "=~", (e, l, r) => ObjectHelper.TypeCoerceEquals(e, l, r, ignoreCase: true));

    /// <summary>Not equal after type coercion.</summary>
    public static IPredicate IsNotEqualTo(this IExpression left, object? right) =>
        Binary(left, right, "!=", (e, l, r) => !ObjectHelper.TypeCoerceEquals(e, l, r));

    /// <summary>Greater than after type coercion (null is less than any value).</summary>
    public static IPredicate IsGreaterThan(this IExpression left, object? right) =>
        Binary(left, right, ">", (e, l, r) => ObjectHelper.TypeCoerceCompare(e, l, r) > 0);

    /// <summary>Greater than or equal after type coercion (null is less than any value).</summary>
    public static IPredicate IsGreaterThanOrEqualTo(this IExpression left, object? right) =>
        Binary(left, right, ">=", (e, l, r) => ObjectHelper.TypeCoerceCompare(e, l, r) >= 0);

    /// <summary>Less than after type coercion (null is less than any value).</summary>
    public static IPredicate IsLessThan(this IExpression left, object? right) =>
        Binary(left, right, "<", (e, l, r) => ObjectHelper.TypeCoerceCompare(e, l, r) < 0);

    /// <summary>Less than or equal after type coercion (null is less than any value).</summary>
    public static IPredicate IsLessThanOrEqualTo(this IExpression left, object? right) =>
        Binary(left, right, "<=", (e, l, r) => ObjectHelper.TypeCoerceCompare(e, l, r) <= 0);

    /// <summary>The value is null.</summary>
    public static IPredicate IsNull(this IExpression expression) =>
        new FuncPredicate(e => expression.Evaluate(e) is null, $"{expression} == null");

    /// <summary>The value is not null.</summary>
    public static IPredicate IsNotNull(this IExpression expression) =>
        new FuncPredicate(e => expression.Evaluate(e) is not null, $"{expression} != null");

    /// <summary>A collection value contains an element equal to <paramref name="value"/>, or the string value contains its string form.</summary>
    public static IPredicate Contains(this IExpression expression, object? value) =>
        Binary(expression, value, "contains", (e, l, r) => ObjectHelper.TypeCoerceContains(e, l, r));

    /// <summary>The string value starts with the string form of <paramref name="prefix"/> (ordinal).</summary>
    public static IPredicate StartsWith(this IExpression expression, object? prefix) =>
        Binary(expression, prefix, "startsWith", (e, l, r) =>
            ObjectHelper.ToText(e, l) is { } text && ObjectHelper.ToText(e, r) is { } p && text.StartsWith(p, StringComparison.Ordinal));

    /// <summary>The string value ends with the string form of <paramref name="suffix"/> (ordinal).</summary>
    public static IPredicate EndsWith(this IExpression expression, object? suffix) =>
        Binary(expression, suffix, "endsWith", (e, l, r) =>
            ObjectHelper.ToText(e, l) is { } text && ObjectHelper.ToText(e, r) is { } s && text.EndsWith(s, StringComparison.Ordinal));

    /// <summary>The whole string value matches the regular expression <paramref name="pattern"/> (like Java's <c>matches()</c>).</summary>
    public static IPredicate Regex(this IExpression expression, string pattern)
    {
        var regex = new Regex(@"\A(?:" + pattern + @")\z", RegexOptions.CultureInvariant);
        return new FuncPredicate(
            e => ObjectHelper.ToText(e, expression.Evaluate(e)) is { } text && regex.IsMatch(text),
            $"{expression} regex '{pattern}'");
    }

    /// <summary>The value is an instance of <typeparamref name="T"/>.</summary>
    public static IPredicate IsInstanceOf<T>(this IExpression expression) =>
        new FuncPredicate(e => expression.Evaluate(e) is T, $"{expression} is {typeof(T).Name}");

    /// <summary>The value equals (after type coercion) any of <paramref name="values"/> (constants or expressions).</summary>
    public static IPredicate In(this IExpression expression, params object?[] values)
    {
        var expressions = values.Select(Expr.Of).ToArray();
        return new FuncPredicate(
            e =>
            {
                var value = expression.Evaluate(e);
                return expressions.Any(x => ObjectHelper.TypeCoerceEquals(e, value, x.Evaluate(e)));
            },
            $"{expression} in ({string.Join(", ", expressions.Select(x => x.ToString()))})");
    }

    /// <summary>Both predicates match (short-circuit).</summary>
    public static IPredicate And(this IPredicate left, IPredicate right) => Pred.And(left, right);

    /// <summary>Either predicate matches (short-circuit).</summary>
    public static IPredicate Or(this IPredicate left, IPredicate right) => Pred.Or(left, right);

    /// <summary>Negates the predicate.</summary>
    public static IPredicate Not(this IPredicate predicate) => Pred.Not(predicate);

    private static FuncPredicate Binary(IExpression left, object? right, string op, Func<Exchange, object?, object?, bool> test)
    {
        var rightExpression = Expr.Of(right);
        return new FuncPredicate(
            e => test(e, left.Evaluate(e), rightExpression.Evaluate(e)),
            $"{left} {op} {rightExpression}");
    }
}
