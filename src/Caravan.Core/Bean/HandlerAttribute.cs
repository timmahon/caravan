namespace Caravan;

/// <summary>Marks the method of a bean to call when the bean is invoked without a method name (Camel's <c>@Handler</c>); see the <c>bean</c> EIP.</summary>
[AttributeUsage(AttributeTargets.Method, Inherited = true, AllowMultiple = false)]
public sealed class HandlerAttribute : Attribute
{
}
