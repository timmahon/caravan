namespace Caravan;

/// <summary>Binds the parameter of a bean method to the message headers dictionary (Camel's <c>@Headers</c>).</summary>
[AttributeUsage(AttributeTargets.Parameter, Inherited = true, AllowMultiple = false)]
public sealed class HeadersAttribute : Attribute
{
}
