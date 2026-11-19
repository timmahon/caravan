namespace Caravan;

/// <summary>Binds the parameter of a bean method to a message header, converted to the parameter type (Camel's <c>@Header</c>).</summary>
[AttributeUsage(AttributeTargets.Parameter, Inherited = true, AllowMultiple = false)]
public sealed class HeaderAttribute(string name) : Attribute
{
    /// <summary>The header name.</summary>
    public string Name { get; } = name;
}
