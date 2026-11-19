namespace Caravan;

/// <summary>Binds the parameter of a bean method to an exchange property, converted to the parameter type (Camel's <c>@ExchangeProperty</c>).</summary>
[AttributeUsage(AttributeTargets.Parameter, Inherited = true, AllowMultiple = false)]
public sealed class ExchangePropertyAttribute(string name) : Attribute
{
    /// <summary>The property name.</summary>
    public string Name { get; } = name;
}
