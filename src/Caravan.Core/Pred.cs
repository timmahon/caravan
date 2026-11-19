namespace Caravan;

/// <summary>
/// Factory methods for predicates (Camel's <c>PredicateBuilder</c>). Comparisons on values are extension
/// methods on <see cref="IExpression"/>, see <see cref="PredicateExtensions"/>.
/// </summary>
public static class Pred
{
    /// <summary>A predicate computed by <paramref name="func"/>; <paramref name="text"/> is used by <c>ToString</c>.</summary>
    public static IPredicate From(Func<Exchange, bool> func, string? text = null) => new FuncPredicate(func, text);

    /// <summary>A predicate on the body converted to <typeparamref name="T"/>, e.g. <c>Pred.Body((Order o) =&gt; o.Total &gt; 0)</c>.</summary>
    public static IPredicate Body<T>(Func<T, bool> predicate) =>
        new FuncPredicate(e => predicate(e.Message.Body is null ? default! : e.Message.GetBody<T>()!), $"body({typeof(T).Name})");

    /// <summary>A predicate on the header <paramref name="name"/> converted to <typeparamref name="T"/> (default when missing).</summary>
    public static IPredicate Header<T>(string name, Func<T?, bool> predicate) =>
        new FuncPredicate(e => predicate(e.Message.GetHeader(name) is null ? default : e.Message.GetHeader<T>(name)), $"header({name},{typeof(T).Name})");

    /// <summary>
    /// A Simple language predicate (Camel's <c>simple("...")</c> as a predicate), e.g.
    /// <c>Pred.Simple("${header.amount} &gt; 100 &amp;&amp; ${body} contains 'vip'")</c>. Compiled once and cached; see
    /// <see cref="Language.SimpleLanguage"/>.
    /// </summary>
    /// <exception cref="Language.Simple.SimpleParseException">The text is invalid.</exception>
    public static IPredicate Simple(string text) => Language.SimpleLanguage.Predicate(text);

    /// <summary>A predicate that always returns <paramref name="value"/>.</summary>
    public static IPredicate Constant(bool value) => new FuncPredicate(_ => value, value ? "true" : "false");

    /// <summary>Uses an expression as a predicate (see <see cref="ExpressionExtensions.ToPredicate"/>).</summary>
    public static IPredicate Of(IExpression expression) => expression.ToPredicate();

    /// <summary>All predicates match (true when there are none). Evaluation stops at the first that does not match.</summary>
    public static IPredicate And(params IPredicate[] predicates) =>
        new FuncPredicate(e => predicates.All(p => p.Matches(e)), "(" + string.Join(" && ", predicates.Select(p => p.ToString())) + ")");

    /// <summary>Any predicate matches (false when there are none). Evaluation stops at the first that matches.</summary>
    public static IPredicate Or(params IPredicate[] predicates) =>
        new FuncPredicate(e => predicates.Any(p => p.Matches(e)), "(" + string.Join(" || ", predicates.Select(p => p.ToString())) + ")");

    /// <summary>Any of the predicates matches (Camel's <c>in(predicates)</c>; same as <see cref="Or"/>).</summary>
    public static IPredicate In(params IPredicate[] predicates) => Or(predicates);

    /// <summary>Negates <paramref name="predicate"/>.</summary>
    public static IPredicate Not(IPredicate predicate) => new FuncPredicate(e => !predicate.Matches(e), $"!({predicate})");
}
