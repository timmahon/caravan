using Caravan.Model;

namespace Caravan.Builder;

/// <summary>DSL for <c>doTry</c>/<c>doCatch</c>/<c>doFinally</c>.</summary>
public static class TryDsl
{
    /// <summary>
    /// Runs <paramref name="tryBlock"/> with Java-like try/catch/finally semantics:
    /// <c>.DoTry(t =&gt; t.To("direct:risky"), c =&gt; c.DoCatch&lt;IOException&gt;(r =&gt; r.To("mock:io")).DoFinally(r =&gt; r.To("mock:finally")))</c>.
    /// The route's error handler (redelivery, <c>onException</c>) does not apply inside the doTry.
    /// </summary>
    public static T DoTry<T>(this T steps, Action<RouteSteps> tryBlock, Action<TryBuilder> clauses)
        where T : RouteSteps
    {
        var definition = steps.AddStep(new TryDefinition());
        steps.BuildNested(definition.Outputs, definition, tryBlock);
        clauses(new TryBuilder(steps, definition));
        return steps;
    }
}
