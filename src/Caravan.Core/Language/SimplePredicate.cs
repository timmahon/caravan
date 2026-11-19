namespace Caravan.Language;

/// <summary>A compiled Simple predicate (see <see cref="SimpleLanguage"/>); create with <see cref="Pred.Simple(string)"/>.</summary>
public sealed class SimplePredicate : IPredicate
{
    internal SimplePredicate(string text, CompiledSimple compiled)
    {
        Text = text;
        Compiled = compiled;
    }

    /// <summary>The predicate text.</summary>
    public string Text { get; }

    internal CompiledSimple Compiled { get; }

    /// <inheritdoc />
    public bool Matches(Exchange exchange) =>
        Compiled.Resolve(Text, exchange, t => SimpleLanguage.Predicate(t).Compiled).Matches(exchange);

    /// <inheritdoc />
    public override string ToString() => "simple{" + Text + "}";
}
