using System.Reflection;

namespace Caravan;

/// <summary>
/// The bean binding found several methods that could be called for the exchange (Camel's <c>AmbiguousMethodCallException</c>); give the
/// method name, parameter types (<c>method=foo(string, int)</c>) or mark one method with <see cref="HandlerAttribute"/>.
/// </summary>
public class AmbiguousMethodCallException(Exchange? exchange, IReadOnlyList<MethodInfo> methods)
    : CaravanExchangeException(
        $"Ambiguous method invocations possible: {string.Join(", ", methods.Select(Describe))}",
        exchange)
{
    /// <summary>The candidate methods.</summary>
    public IReadOnlyList<MethodInfo> Methods { get; } = methods;

    internal static string Describe(MethodInfo method) =>
        $"{method.DeclaringType?.Name}.{method.Name}({string.Join(", ", method.GetParameters().Select(p => p.ParameterType.Name))})";
}
