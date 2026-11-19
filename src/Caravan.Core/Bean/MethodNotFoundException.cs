namespace Caravan;

/// <summary>A bean has no public method matching the requested name or parameters (Camel's <c>MethodNotFoundException</c>).</summary>
public class MethodNotFoundException(Type beanType, string methodName, string? detail = null)
    : CaravanException($"Method with name: {methodName} not found on bean type: {beanType.FullName}{(detail is null ? string.Empty : ". " + detail)}")
{
    /// <summary>The bean type.</summary>
    public Type BeanType { get; } = beanType;

    /// <summary>The method name (or specification) that was looked up.</summary>
    public string MethodName { get; } = methodName;
}
