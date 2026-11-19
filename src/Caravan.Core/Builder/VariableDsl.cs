using Caravan.Model;

namespace Caravan.Builder;

/// <summary>
/// DSL for Camel 4 variables: <c>setVariable</c>, <c>setVariables</c>, <c>removeVariable</c>, <c>convertVariableTo</c>. Names without prefix are
/// exchange variables; <c>global:name</c>, <c>route:name</c> (<c>route:routeId:name</c>) and <c>repositoryId:name</c> address repositories.
/// </summary>
public static class VariableDsl
{
    /// <summary>Sets the variable <paramref name="name"/> to the value of <paramref name="expression"/>.</summary>
    /// <param name="steps">The block to add to.</param>
    /// <param name="name">The variable name.</param>
    /// <param name="expression">Computes the value.</param>
    /// <returns>The block.</returns>
    public static T SetVariable<T>(this T steps, string name, IExpression expression)
        where T : RouteSteps
    {
        steps.AddStep(new SetVariableDefinition(name, expression));
        return steps;
    }

    /// <summary>Sets a variable from a function of the exchange.</summary>
    /// <param name="steps">The block to add to.</param>
    /// <param name="name">The variable name.</param>
    /// <param name="expression">Computes the value.</param>
    /// <returns>The block.</returns>
    public static T SetVariable<T>(this T steps, string name, Func<Exchange, object?> expression)
        where T : RouteSteps => steps.SetVariable(name, new FuncExpression(expression));

    /// <summary>Sets a variable from a function of the body converted to <typeparamref name="TIn"/>: <c>.SetVariable("len", (string s) =&gt; s.Length)</c>.</summary>
    /// <param name="steps">The block to add to.</param>
    /// <param name="name">The variable name.</param>
    /// <param name="expression">Computes the value.</param>
    /// <returns>The block.</returns>
    public static T SetVariable<T, TIn>(this T steps, string name, Func<TIn, object?> expression)
        where T : RouteSteps => steps.SetVariable(name, Expr.Body(expression));

    /// <summary>Sets several variables, in order (each expression sees the variables set before it).</summary>
    /// <param name="steps">The block to add to.</param>
    /// <param name="variables">The variable names and expressions.</param>
    /// <returns>The block.</returns>
    public static T SetVariables<T>(this T steps, params (string Name, IExpression Expression)[] variables)
        where T : RouteSteps
    {
        var definition = new SetVariablesDefinition();
        definition.Variables.AddRange(variables.Select(v => new KeyValuePair<string, IExpression>(v.Name, v.Expression)));
        steps.AddStep(definition);
        return steps;
    }

    /// <summary>Sets several variables to constant values, in order.</summary>
    /// <param name="steps">The block to add to.</param>
    /// <param name="variables">The variable names and values.</param>
    /// <returns>The block.</returns>
    public static T SetVariables<T>(this T steps, IEnumerable<KeyValuePair<string, object?>> variables)
        where T : RouteSteps =>
        steps.SetVariables(variables.Select(v => (v.Key, Expr.Of(v.Value))).ToArray());

    /// <summary>Removes the variables matching <paramref name="name"/>: a name, <c>*</c>, a wildcard or a regular expression (<c>global:*</c>).</summary>
    /// <param name="steps">The block to add to.</param>
    /// <param name="name">The name or pattern.</param>
    /// <returns>The block.</returns>
    public static T RemoveVariable<T>(this T steps, string name)
        where T : RouteSteps
    {
        steps.AddStep(new RemoveVariableDefinition(name));
        return steps;
    }

    /// <summary>Converts the variable <paramref name="name"/> to <paramref name="type"/>, e.g. <c>.ConvertVariableTo("n", typeof(int))</c>.</summary>
    /// <param name="steps">The block to add to.</param>
    /// <param name="name">The variable to convert.</param>
    /// <param name="type">The target type.</param>
    /// <param name="toName">The variable receiving the result (default: <paramref name="name"/>).</param>
    /// <param name="mandatory">Whether a failed conversion fails the exchange (otherwise the variable becomes null).</param>
    /// <returns>The block.</returns>
    public static T ConvertVariableTo<T>(this T steps, string name, Type type, string? toName = null, bool mandatory = true)
        where T : RouteSteps
    {
        steps.AddStep(new ConvertVariableDefinition(name, type) { ToName = toName, Mandatory = mandatory });
        return steps;
    }

    /// <summary>
    /// Sends to <paramref name="uri"/> using variables (Camel's <c>toV</c>): the body sent is the variable <paramref name="variableSend"/> (when
    /// set) and the reply is stored in the variable <paramref name="variableReceive"/> (body; headers as <c>name.header.key</c>), leaving the
    /// message unchanged. With only a send variable the original body is restored after an <c>InOnly</c> send.
    /// </summary>
    /// <param name="steps">The block to add to.</param>
    /// <param name="uri">The endpoint URI.</param>
    /// <param name="variableSend">The variable to send as the body, or null.</param>
    /// <param name="variableReceive">The variable receiving the reply, or null.</param>
    /// <param name="pattern">The exchange pattern to send with; null keeps the exchange's pattern.</param>
    /// <returns>The block.</returns>
    public static T ToV<T>(this T steps, string uri, string? variableSend, string? variableReceive, ExchangePattern? pattern = null)
        where T : RouteSteps
    {
        steps.AddStep(new ToDefinition(uri, pattern) { VariableSend = variableSend, VariableReceive = variableReceive });
        return steps;
    }
}
