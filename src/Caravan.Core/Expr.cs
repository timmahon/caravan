namespace Caravan;

/// <summary>Factory methods for common expressions (Camel's <c>ExpressionBuilder</c>/<c>Builder</c>).</summary>
public static class Expr
{
    /// <summary>The message body (<c>${body}</c>).</summary>
    public static IExpression Body() => new FuncExpression(e => e.Message.Body, "${body}");

    /// <summary>The message body converted to <typeparamref name="T"/>.</summary>
    public static IExpression Body<T>() => new FuncExpression(e => e.Message.GetBody<T>(), $"${{bodyAs({typeof(T).Name})}}");

    /// <summary>The header <paramref name="name"/> (<c>${header.name}</c>).</summary>
    public static IExpression Header(string name) => new FuncExpression(e => e.Message.GetHeader(name), $"${{header.{name}}}");

    /// <summary>The exchange property <paramref name="name"/> (<c>${exchangeProperty.name}</c>).</summary>
    public static IExpression Property(string name) => new FuncExpression(e => e.GetProperty(name), $"${{exchangeProperty.{name}}}");

    /// <summary>A constant value.</summary>
    public static IExpression Constant(object? value) => new FuncExpression(_ => value, value?.ToString() ?? "null");

    /// <summary>The exchange id.</summary>
    public static IExpression ExchangeId() => new FuncExpression(e => e.ExchangeId, "${exchangeId}");

    /// <summary>The message id.</summary>
    public static IExpression MessageId() => new FuncExpression(e => e.Message.MessageId, "${messageId}");

    /// <summary>The exception on the exchange, if any.</summary>
    public static IExpression Exception() => new FuncExpression(e => e.Exception, "${exception}");

    /// <summary>An expression computed by <paramref name="func"/>; <paramref name="text"/> is used by <c>ToString</c>.</summary>
    public static IExpression From(Func<Exchange, object?> func, string? text = null) => new FuncExpression(func, text);

    /// <summary>A predicate computed by <paramref name="func"/>; <paramref name="text"/> is used by <c>ToString</c>.</summary>
    public static IPredicate Predicate(Func<Exchange, bool> func, string? text = null) => new FuncPredicate(func, text);
}
