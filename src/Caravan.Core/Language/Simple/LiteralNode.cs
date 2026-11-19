namespace Caravan.Language.Simple;

/// <summary>A constant: text, a number, a boolean or null.</summary>
internal sealed class LiteralNode(object? value) : SimpleNode
{
    /// <summary>The constant.</summary>
    public object? Value { get; } = value;

    /// <inheritdoc />
    public override object? Evaluate(Exchange exchange) => Value;

    /// <inheritdoc />
    public override string ToString() => Value is string s ? $"'{s}'" : Value?.ToString() ?? "null";
}
