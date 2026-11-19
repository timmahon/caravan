using Caravan.Model;

namespace Caravan.Builder;

/// <summary>DSL for the <c>filter</c> EIP: <c>.Filter(predicate, f =&gt; f.To("mock:matched"))</c>.</summary>
public static class FilterDsl
{
    /// <summary>Runs <paramref name="block"/> only for exchanges matching <paramref name="predicate"/>; routing continues after the filter for all exchanges.</summary>
    public static T Filter<T>(this T steps, IPredicate predicate, Action<RouteSteps> block)
        where T : RouteSteps
    {
        var definition = steps.AddStep(new FilterDefinition(predicate));
        steps.BuildNested(definition.Outputs, definition, block);
        return steps;
    }

    /// <summary>Runs <paramref name="block"/> only for exchanges matching <paramref name="predicate"/>.</summary>
    public static T Filter<T>(this T steps, Func<Exchange, bool> predicate, Action<RouteSteps> block)
        where T : RouteSteps => steps.Filter(Pred.From(predicate), block);

    /// <summary>
    /// Runs <paramref name="block"/> only when the body, converted to <typeparamref name="TBody"/>, matches
    /// <paramref name="predicate"/>: <c>.Filter((Order o) =&gt; o.Total &gt; 0, f =&gt; ...)</c>.
    /// </summary>
    public static T Filter<T, TBody>(this T steps, Func<TBody, bool> predicate, Action<RouteSteps> block)
        where T : RouteSteps => steps.Filter(Pred.Body(predicate), block);
}
