namespace Caravan;

/// <summary>Evaluates a value from an exchange (Camel's <c>Expression</c>).</summary>
public interface IExpression
{
    object? Evaluate(Exchange exchange);
}

/// <summary>Evaluates a condition on an exchange (Camel's <c>Predicate</c>).</summary>
public interface IPredicate
{
    bool Matches(Exchange exchange);
}

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

/// <summary>Helpers for evaluating predicates.</summary>
public static class PredicateHelper
{
    /// <summary>Camel's truthiness rules: booleans as-is, "true"/"false" strings parsed, null is false, anything else is true.</summary>
    public static bool ToBoolean(Exchange exchange, object? value) => value switch
    {
        null => false,
        bool b => b,
        string s when bool.TryParse(s.Trim(), out var parsed) => parsed,
        string s => s.Length > 0,
        _ => true,
    };
}

/// <summary>An expression backed by a function.</summary>
public sealed class FuncExpression(Func<Exchange, object?> func, string? text = null) : IExpression
{
    public object? Evaluate(Exchange exchange) => func(exchange);

    public override string ToString() => text ?? "expression";
}

/// <summary>A predicate backed by a function.</summary>
public sealed class FuncPredicate(Func<Exchange, bool> func, string? text = null) : IPredicate
{
    public bool Matches(Exchange exchange) => func(exchange);

    public override string ToString() => text ?? "predicate";
}

/// <summary>Factory methods for common expressions (Camel's <c>ExpressionBuilder</c>/<c>Builder</c>).</summary>
public static class Expr
{
    public static IExpression Body() => new FuncExpression(e => e.Message.Body, "${body}");

    public static IExpression Body<T>() => new FuncExpression(e => e.Message.GetBody<T>(), $"${{bodyAs({typeof(T).Name})}}");

    public static IExpression Header(string name) => new FuncExpression(e => e.Message.GetHeader(name), $"${{header.{name}}}");

    public static IExpression Property(string name) => new FuncExpression(e => e.GetProperty(name), $"${{exchangeProperty.{name}}}");

    public static IExpression Constant(object? value) => new FuncExpression(_ => value, value?.ToString() ?? "null");

    public static IExpression ExchangeId() => new FuncExpression(e => e.ExchangeId, "${exchangeId}");

    public static IExpression MessageId() => new FuncExpression(e => e.Message.MessageId, "${messageId}");

    public static IExpression Exception() => new FuncExpression(e => e.Exception, "${exception}");

    public static IExpression From(Func<Exchange, object?> func, string? text = null) => new FuncExpression(func, text);

    public static IPredicate Predicate(Func<Exchange, bool> func, string? text = null) => new FuncPredicate(func, text);
}
