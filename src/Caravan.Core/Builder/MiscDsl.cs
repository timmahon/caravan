using Caravan.Model;

namespace Caravan.Builder;

/// <summary>DSL for small EIPs: <c>validate</c>, <c>script</c>, <c>setExchangePattern</c>, <c>sort</c> and <c>setHeaders</c>.</summary>
public static class MiscDsl
{
    /// <summary>Fails the exchange with a <see cref="PredicateValidationException"/> when <paramref name="predicate"/> does not match.</summary>
    /// <param name="steps">The block to add to.</param>
    /// <param name="predicate">The condition the exchange must meet.</param>
    public static T Validate<T>(this T steps, IPredicate predicate)
        where T : RouteSteps
    {
        steps.AddStep(new ValidateDefinition(predicate));
        return steps;
    }

    /// <summary>Fails the exchange with a <see cref="PredicateValidationException"/> when <paramref name="predicate"/> returns false.</summary>
    /// <param name="steps">The block to add to.</param>
    /// <param name="predicate">The condition the exchange must meet.</param>
    public static T Validate<T>(this T steps, Func<Exchange, bool> predicate)
        where T : RouteSteps => steps.Validate(Pred.From(predicate));

    /// <summary>Fails the exchange when the body, converted to <typeparamref name="TBody"/>, does not match: <c>.Validate((Order o) =&gt; o.Total &gt; 0)</c>.</summary>
    /// <param name="steps">The block to add to.</param>
    /// <param name="predicate">The condition on the body.</param>
    public static T Validate<T, TBody>(this T steps, Func<TBody, bool> predicate)
        where T : RouteSteps => steps.Validate(Pred.Body(predicate));

    /// <summary>Evaluates <paramref name="expression"/> for its side effects; the result is ignored (Camel's <c>script</c>).</summary>
    /// <param name="steps">The block to add to.</param>
    /// <param name="expression">The expression.</param>
    public static T Script<T>(this T steps, IExpression expression)
        where T : RouteSteps
    {
        steps.AddStep(new ScriptDefinition(expression));
        return steps;
    }

    /// <summary>Evaluates <paramref name="expression"/> for its side effects; the result is ignored (Camel's <c>script</c>).</summary>
    /// <param name="steps">The block to add to.</param>
    /// <param name="expression">The expression.</param>
    public static T Script<T>(this T steps, Func<Exchange, object?> expression)
        where T : RouteSteps => steps.Script(new FuncExpression(expression));

    /// <summary>Sets the exchange pattern for the rest of the route.</summary>
    /// <param name="steps">The block to add to.</param>
    /// <param name="pattern">The pattern.</param>
    public static T SetExchangePattern<T>(this T steps, ExchangePattern pattern)
        where T : RouteSteps
    {
        steps.AddStep(new SetExchangePatternDefinition(pattern));
        return steps;
    }

    /// <summary>
    /// Sorts the items of <paramref name="expression"/> (by default the body) and sets the body to the sorted list:
    /// <c>.Sort(Expr.Tokenize(","))</c>. Without a comparer items are compared like Camel's natural ordering (numbers numerically).
    /// </summary>
    /// <param name="steps">The block to add to.</param>
    /// <param name="expression">Computes the items; null for the body.</param>
    /// <param name="comparer">Orders the items; null for natural ordering.</param>
    public static T Sort<T>(this T steps, IExpression? expression = null, IComparer<object?>? comparer = null)
        where T : RouteSteps
    {
        steps.AddStep(new SortDefinition { Expression = expression, Comparer = comparer });
        return steps;
    }

    /// <summary>Sorts the items (cast to <typeparamref name="TItem"/>) of <paramref name="expression"/> (by default the body) with <paramref name="comparison"/>: <c>.Sort((string a, string b) =&gt; b.CompareTo(a))</c>.</summary>
    /// <param name="steps">The block to add to.</param>
    /// <param name="comparison">Orders the items.</param>
    /// <param name="expression">Computes the items; null for the body.</param>
    public static T Sort<T, TItem>(this T steps, Func<TItem, TItem, int> comparison, IExpression? expression = null)
        where T : RouteSteps
    {
        ArgumentNullException.ThrowIfNull(comparison);
        return steps.Sort(expression, Comparer<object?>.Create((x, y) => comparison((TItem)x!, (TItem)y!)));
    }

    /// <summary>Sets several headers, in order (each expression sees the headers set before it): <c>.SetHeaders(("a", Expr.Constant(1)), ("b", Expr.Header("a")))</c>.</summary>
    /// <param name="steps">The block to add to.</param>
    /// <param name="headers">The header names and expressions.</param>
    public static T SetHeaders<T>(this T steps, params (string Name, IExpression Expression)[] headers)
        where T : RouteSteps
    {
        var definition = new SetHeadersDefinition();
        definition.Headers.AddRange(headers.Select(h => new KeyValuePair<string, IExpression>(h.Name, h.Expression)));
        steps.AddStep(definition);
        return steps;
    }

    /// <summary>Sets several headers to constant values, in order: <c>.SetHeaders(new Dictionary&lt;string, object?&gt; { ["a"] = 1 })</c>.</summary>
    /// <param name="steps">The block to add to.</param>
    /// <param name="headers">The header names and values.</param>
    public static T SetHeaders<T>(this T steps, IEnumerable<KeyValuePair<string, object?>> headers)
        where T : RouteSteps =>
        steps.SetHeaders(headers.Select(h => (h.Key, Expr.Of(h.Value))).ToArray());
}
