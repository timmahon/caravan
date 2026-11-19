namespace Caravan.Components.Bean;

/// <summary>One parameter of a method specification (<c>method=foo(${body}, 'x', 5)</c> or <c>method=foo(string, int)</c>).</summary>
internal sealed class BeanParameterToken
{
    private BeanParameterToken(BeanParameterTokenKind kind, string text, Type? type, object? value, IExpression? expression)
    {
        Kind = kind;
        Text = text;
        Type = type;
        Value = value;
        Expression = expression;
    }

    public BeanParameterTokenKind Kind { get; }

    public string Text { get; }

    /// <summary>The type of a <see cref="BeanParameterTokenKind.Type"/> token.</summary>
    public Type? Type { get; }

    /// <summary>The value of a <see cref="BeanParameterTokenKind.Literal"/> token.</summary>
    public object? Value { get; }

    /// <summary>The expression of a <see cref="BeanParameterTokenKind.Simple"/> token.</summary>
    public IExpression? Expression { get; }

    public bool IsTypeToken => Kind is BeanParameterTokenKind.Type or BeanParameterTokenKind.AnyType;

    public static BeanParameterToken Parse(string text)
    {
        var t = text.Trim();
        if (t == "*")
        {
            return new(BeanParameterTokenKind.AnyType, t, null, null, null);
        }

        if (t.Contains("${", StringComparison.Ordinal))
        {
            return new(BeanParameterTokenKind.Simple, t, null, null, Language.SimpleLanguage.Expression(t));
        }

        if (t.Length >= 2 && ((t[0] == '\'' && t[^1] == '\'') || (t[0] == '"' && t[^1] == '"')))
        {
            return Literal(t, t[1..^1]);
        }

        switch (t)
        {
            case "null":
                return Literal(t, null);
            case "true":
                return Literal(t, true);
            case "false":
                return Literal(t, false);
        }

        if (long.TryParse(t, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var number))
        {
            return Literal(t, number);
        }

        if (double.TryParse(t, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var real))
        {
            return Literal(t, real);
        }

        return Language.Simple.TypeNameResolver.Resolve(t) is { } type
            ? new(BeanParameterTokenKind.Type, t, type, null, null)
            : Literal(t, t);
    }

    /// <summary>A type token used as a value (when the specification mixes types and values): its text.</summary>
    public BeanParameterToken AsLiteral() => IsTypeToken ? Literal(Text, Text) : this;

    public override string ToString() => Text;

    private static BeanParameterToken Literal(string text, object? value) => new(BeanParameterTokenKind.Literal, text, null, value, null);
}
