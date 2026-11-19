using Caravan.Model;

namespace Caravan.Builder;

/// <summary>DSL for exchange properties: <c>setProperty</c>, <c>removeProperty</c>, <c>removeProperties</c>.</summary>
public static class PropertyDsl
{
    /// <summary>Sets the exchange property <paramref name="name"/> to the value of <paramref name="expression"/>.</summary>
    public static T SetProperty<T>(this T steps, string name, IExpression expression)
        where T : RouteSteps
    {
        steps.AddStep(new SetPropertyDefinition(name, expression));
        return steps;
    }

    /// <summary>Sets an exchange property from a function of the exchange.</summary>
    public static T SetProperty<T>(this T steps, string name, Func<Exchange, object?> expression)
        where T : RouteSteps => steps.SetProperty(name, new FuncExpression(expression));

    /// <summary>Sets an exchange property from a function of the body converted to <typeparamref name="TIn"/>: <c>.SetProperty("len", (string s) =&gt; s.Length)</c>.</summary>
    public static T SetProperty<T, TIn>(this T steps, string name, Func<TIn, object?> expression)
        where T : RouteSteps => steps.SetProperty(name, Expr.Body(expression));

    /// <summary>Removes the exchange property <paramref name="name"/>.</summary>
    public static T RemoveProperty<T>(this T steps, string name)
        where T : RouteSteps
    {
        steps.AddStep(new RemovePropertyDefinition(name));
        return steps;
    }

    /// <summary>
    /// Removes the exchange properties matching <paramref name="pattern"/> (exact name, wildcard <c>foo*</c> or regex),
    /// except those matching one of <paramref name="excludePatterns"/>.
    /// </summary>
    public static T RemoveProperties<T>(this T steps, string pattern, params string[] excludePatterns)
        where T : RouteSteps
    {
        steps.AddStep(new RemovePropertiesDefinition(pattern, excludePatterns));
        return steps;
    }
}
