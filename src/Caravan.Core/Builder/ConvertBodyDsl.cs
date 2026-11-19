using Caravan.Model;

namespace Caravan.Builder;

/// <summary>DSL for the <c>convertBodyTo</c> EIP.</summary>
public static class ConvertBodyDsl
{
    /// <summary>
    /// Converts the body to <paramref name="type"/> with the context's type converters, e.g.
    /// <c>.ConvertBodyTo(typeof(int))</c>. A null body is left as is.
    /// </summary>
    /// <param name="steps">The block to add to.</param>
    /// <param name="type">The target type.</param>
    /// <param name="charset">The charset used between text and bytes/streams (e.g. <c>UTF-16</c>); null uses UTF-8.</param>
    /// <param name="mandatory">
    /// True (default) to fail with <see cref="InvalidPayloadException"/> when the body cannot be converted; false to set
    /// the body to null instead (as Camel does).
    /// </param>
    public static T ConvertBodyTo<T>(this T steps, Type type, string? charset = null, bool mandatory = true)
        where T : RouteSteps
    {
        steps.AddStep(new ConvertBodyDefinition(type) { Charset = charset, Mandatory = mandatory });
        return steps;
    }
}
