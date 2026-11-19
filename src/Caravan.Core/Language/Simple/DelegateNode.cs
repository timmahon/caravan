namespace Caravan.Language.Simple;

/// <summary>A function such as <c>${header.foo}</c>, evaluated by a delegate.</summary>
internal sealed class DelegateNode(Func<Exchange, object?> evaluate, string text, Action<Exchange, object?>? setter = null) : SimpleNode
{
    /// <inheritdoc />
    public override Action<Exchange, object?>? Setter { get; } = setter;

    /// <inheritdoc />
    public override object? Evaluate(Exchange exchange) => evaluate(exchange);

    /// <inheritdoc />
    public override string ToString() => "${" + text + "}";
}
