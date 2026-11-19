namespace Caravan.Language;

/// <summary>A compiled Simple expression (see <see cref="SimpleLanguage"/>); create with <see cref="Expr.Simple(string)"/>.</summary>
public sealed class SimpleExpression : IExpression
{
    internal SimpleExpression(string text, Type? resultType, CompiledSimple compiled)
    {
        Text = text;
        ResultType = resultType;
        Compiled = compiled;
    }

    /// <summary>The expression text.</summary>
    public string Text { get; }

    /// <summary>The type the value is converted to; null keeps the value as is.</summary>
    public Type? ResultType { get; }

    internal CompiledSimple Compiled { get; }

    /// <inheritdoc />
    public object? Evaluate(Exchange exchange)
    {
        var value = Compiled.Resolve(Text, exchange, t => SimpleLanguage.Expression(t).Compiled).Evaluate(exchange);
        return ResultType is null ? value : exchange.Context.TypeConverter.Convert(ResultType, exchange, value);
    }

    /// <inheritdoc />
    public override string ToString() => "simple{" + Text + "}";
}
