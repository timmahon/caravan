using Caravan.Model;

namespace Caravan.Builder;

/// <summary>DSL for headers: typed <c>setHeader</c>, <c>removeHeader</c>, <c>removeHeaders</c>, <c>convertHeaderTo</c>.</summary>
public static class HeaderDsl
{
    /// <summary>Sets a header from a function of the body converted to <typeparamref name="TIn"/>: <c>.SetHeader("len", (string s) =&gt; s.Length)</c>.</summary>
    public static T SetHeader<T, TIn>(this T steps, string name, Func<TIn, object?> expression)
        where T : RouteSteps => steps.SetHeader(name, Expr.Body(expression));

    /// <summary>Removes the header <paramref name="name"/> (case-insensitive).</summary>
    public static T RemoveHeader<T>(this T steps, string name)
        where T : RouteSteps
    {
        steps.AddStep(new RemoveHeaderDefinition(name));
        return steps;
    }

    /// <summary>
    /// Removes the headers matching <paramref name="pattern"/> (exact name, wildcard <c>foo*</c> or regex, ignoring case),
    /// except those matching one of <paramref name="excludePatterns"/>.
    /// </summary>
    public static T RemoveHeaders<T>(this T steps, string pattern, params string[] excludePatterns)
        where T : RouteSteps
    {
        steps.AddStep(new RemoveHeadersDefinition(pattern, excludePatterns));
        return steps;
    }

    /// <summary>
    /// Converts the header <paramref name="name"/> to <paramref name="type"/>, e.g. <c>.ConvertHeaderTo("n", typeof(int))</c>.
    /// A missing header is left as is.
    /// </summary>
    /// <param name="steps">The block to add to.</param>
    /// <param name="name">The header to convert.</param>
    /// <param name="type">The target type.</param>
    /// <param name="toName">Store the converted value under this header instead of replacing <paramref name="name"/>.</param>
    /// <param name="mandatory">
    /// True (default) to fail with <see cref="TypeConversion.NoTypeConversionAvailableException"/> when the value cannot be
    /// converted; false to set the header to null instead.
    /// </param>
    public static T ConvertHeaderTo<T>(this T steps, string name, Type type, string? toName = null, bool mandatory = true)
        where T : RouteSteps
    {
        steps.AddStep(new ConvertHeaderDefinition(name, type) { ToName = toName, Mandatory = mandatory });
        return steps;
    }
}
