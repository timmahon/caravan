using System.Reflection;

namespace Caravan;

/// <summary>A value could not be bound to a parameter of a bean method (Camel's <c>ParameterBindingException</c>).</summary>
public class ParameterBindingException(Exception innerException, MethodInfo method, ParameterInfo parameter, object? value)
    : CaravanException(
        $"Error during parameter binding on method: {AmbiguousMethodCallException.Describe(method)} at parameter #{parameter.Position} " +
        $"({parameter.Name}) with type: {parameter.ParameterType.Name} with value type: {value?.GetType().Name ?? "null"} and value: {value}",
        innerException)
{
    /// <summary>The method.</summary>
    public MethodInfo Method { get; } = method;

    /// <summary>The parameter.</summary>
    public ParameterInfo Parameter { get; } = parameter;

    /// <summary>The value that could not be bound.</summary>
    public object? Value { get; } = value;
}
