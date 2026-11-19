namespace Caravan;

/// <summary>Binds the parameter of a bean method to the message body, converted to the parameter type (Camel's <c>@Body</c>).</summary>
[AttributeUsage(AttributeTargets.Parameter, Inherited = true, AllowMultiple = false)]
public sealed class BodyAttribute : Attribute
{
}
