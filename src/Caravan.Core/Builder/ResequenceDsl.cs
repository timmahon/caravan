using Caravan.Model;

namespace Caravan.Builder;

/// <summary>DSL for the <c>resequence</c> EIP.</summary>
public static class ResequenceDsl
{
    /// <summary>
    /// Reorders exchanges by <paramref name="expression"/> with a batch resequencer (default options: batches of 100, 1 s timeout) and
    /// sends them, in order, through <paramref name="body"/>; the incoming exchanges continue at once.
    /// </summary>
    /// <param name="steps">The block to add to.</param>
    /// <param name="expression">Computes the sort key.</param>
    /// <param name="body">The steps run for each reordered exchange.</param>
    public static T Resequence<T>(this T steps, IExpression expression, Action<RouteSteps> body)
        where T : RouteSteps
        => steps.Resequence(expression, _ => { }, body);

    /// <summary>
    /// Reorders exchanges by <paramref name="expression"/>: <c>.Resequence(Expr.Header("seq"), r =&gt; r.Stream(timeout: TimeSpan.FromSeconds(2)), then =&gt; then.To("mock:result"))</c>.
    /// </summary>
    /// <param name="steps">The block to add to.</param>
    /// <param name="expression">Computes the sort key (batch) or the sequence number (stream).</param>
    /// <param name="configure">Chooses the algorithm and its options; batch by default.</param>
    /// <param name="body">The steps run for each reordered exchange.</param>
    public static T Resequence<T>(this T steps, IExpression expression, Action<ResequenceBuilder> configure, Action<RouteSteps> body)
        where T : RouteSteps
    {
        ArgumentNullException.ThrowIfNull(configure);
        var definition = steps.AddStep(new ResequenceDefinition(expression));
        configure(new ResequenceBuilder(definition));
        steps.BuildNested(definition.Outputs, definition, body);
        return steps;
    }

    /// <summary>Reorders exchanges by the value a function computes, with a batch resequencer with the default options.</summary>
    /// <param name="steps">The block to add to.</param>
    /// <param name="expression">Computes the sort key.</param>
    /// <param name="body">The steps run for each reordered exchange.</param>
    public static T Resequence<T>(this T steps, Func<Exchange, object?> expression, Action<RouteSteps> body)
        where T : RouteSteps
        => steps.Resequence(new FuncExpression(expression), body);

    /// <summary>Reorders exchanges by the value a function computes.</summary>
    /// <param name="steps">The block to add to.</param>
    /// <param name="expression">Computes the sort key (batch) or the sequence number (stream).</param>
    /// <param name="configure">Chooses the algorithm and its options; batch by default.</param>
    /// <param name="body">The steps run for each reordered exchange.</param>
    public static T Resequence<T>(this T steps, Func<Exchange, object?> expression, Action<ResequenceBuilder> configure, Action<RouteSteps> body)
        where T : RouteSteps
        => steps.Resequence(new FuncExpression(expression), configure, body);
}
