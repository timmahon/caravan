using Caravan.Model;

namespace Caravan.Builder;

/// <summary>DSL for the <c>loop</c> EIP: <c>.Loop(3, l =&gt; l.To("mock:a"))</c>, <c>.LoopWhile(predicate, l =&gt; ...)</c>.</summary>
public static class LoopDsl
{
    /// <summary>
    /// Runs <paramref name="block"/> <paramref name="count"/> times. <see cref="ExchangePropertyKeys.LoopIndex"/> holds the 0-based iteration
    /// and <see cref="ExchangePropertyKeys.LoopSize"/> the count.
    /// </summary>
    /// <param name="steps">The block to add to.</param>
    /// <param name="count">The number of iterations.</param>
    /// <param name="block">The steps to repeat.</param>
    /// <param name="copy">Start each iteration from a copy of the exchange as it entered the loop (else each processes the previous output).</param>
    /// <param name="breakOnShutdown">End the loop quietly when the exchange is cancelled or the context stops.</param>
    public static T Loop<T>(this T steps, int count, Action<RouteSteps> block, bool copy = false, bool breakOnShutdown = false)
        where T : RouteSteps => steps.Loop(Expr.Constant(count), block, copy, breakOnShutdown);

    /// <summary>Runs <paramref name="block"/> the number of times computed (once) by <paramref name="count"/>.</summary>
    /// <param name="steps">The block to add to.</param>
    /// <param name="count">Computes the number of iterations (converted to <see cref="int"/>).</param>
    /// <param name="block">The steps to repeat.</param>
    /// <param name="copy">Start each iteration from a copy of the exchange as it entered the loop (else each processes the previous output).</param>
    /// <param name="breakOnShutdown">End the loop quietly when the exchange is cancelled or the context stops.</param>
    public static T Loop<T>(this T steps, IExpression count, Action<RouteSteps> block, bool copy = false, bool breakOnShutdown = false)
        where T : RouteSteps
    {
        var definition = steps.AddStep(new LoopDefinition { Count = count, Copy = copy, BreakOnShutdown = breakOnShutdown });
        steps.BuildNested(definition.Outputs, definition, block);
        return steps;
    }

    /// <summary>Runs <paramref name="block"/> the number of times computed (once) by <paramref name="count"/>.</summary>
    /// <param name="steps">The block to add to.</param>
    /// <param name="count">Computes the number of iterations (converted to <see cref="int"/>).</param>
    /// <param name="block">The steps to repeat.</param>
    /// <param name="copy">Start each iteration from a copy of the exchange as it entered the loop (else each processes the previous output).</param>
    /// <param name="breakOnShutdown">End the loop quietly when the exchange is cancelled or the context stops.</param>
    public static T Loop<T>(this T steps, Func<Exchange, object?> count, Action<RouteSteps> block, bool copy = false, bool breakOnShutdown = false)
        where T : RouteSteps => steps.Loop(new FuncExpression(count), block, copy, breakOnShutdown);

    /// <summary>
    /// Runs <paramref name="block"/> while <paramref name="predicate"/> matches, checked before each iteration on the output of the previous
    /// one (Camel's <c>loopDoWhile</c>). As in Camel there is no iteration limit.
    /// </summary>
    /// <param name="steps">The block to add to.</param>
    /// <param name="predicate">The condition.</param>
    /// <param name="block">The steps to repeat.</param>
    /// <param name="copy">Start each iteration from a copy of the exchange as it entered the loop.</param>
    /// <param name="breakOnShutdown">End the loop quietly when the exchange is cancelled or the context stops.</param>
    public static T LoopWhile<T>(this T steps, IPredicate predicate, Action<RouteSteps> block, bool copy = false, bool breakOnShutdown = false)
        where T : RouteSteps
    {
        var definition = steps.AddStep(new LoopDefinition { While = predicate, Copy = copy, BreakOnShutdown = breakOnShutdown });
        steps.BuildNested(definition.Outputs, definition, block);
        return steps;
    }

    /// <summary>Runs <paramref name="block"/> while <paramref name="predicate"/> matches.</summary>
    /// <param name="steps">The block to add to.</param>
    /// <param name="predicate">The condition.</param>
    /// <param name="block">The steps to repeat.</param>
    /// <param name="copy">Start each iteration from a copy of the exchange as it entered the loop.</param>
    /// <param name="breakOnShutdown">End the loop quietly when the exchange is cancelled or the context stops.</param>
    public static T LoopWhile<T>(this T steps, Func<Exchange, bool> predicate, Action<RouteSteps> block, bool copy = false, bool breakOnShutdown = false)
        where T : RouteSteps => steps.LoopWhile(Pred.From(predicate), block, copy, breakOnShutdown);

    /// <summary>Runs <paramref name="block"/> while the body, converted to <typeparamref name="TBody"/>, matches <paramref name="predicate"/>: <c>.LoopWhile((string s) =&gt; s.Length &lt; 5, l =&gt; ...)</c>.</summary>
    /// <param name="steps">The block to add to.</param>
    /// <param name="predicate">The condition on the body.</param>
    /// <param name="block">The steps to repeat.</param>
    /// <param name="copy">Start each iteration from a copy of the exchange as it entered the loop.</param>
    /// <param name="breakOnShutdown">End the loop quietly when the exchange is cancelled or the context stops.</param>
    public static T LoopWhile<T, TBody>(this T steps, Func<TBody, bool> predicate, Action<RouteSteps> block, bool copy = false, bool breakOnShutdown = false)
        where T : RouteSteps => steps.LoopWhile(Pred.Body(predicate), block, copy, breakOnShutdown);
}
