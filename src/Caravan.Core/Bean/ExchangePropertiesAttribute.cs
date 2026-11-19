namespace Caravan;

/// <summary>Binds the parameter of a bean method to the exchange properties dictionary (Camel's <c>@ExchangeProperties</c>).</summary>
[AttributeUsage(AttributeTargets.Parameter, Inherited = true, AllowMultiple = false)]
public sealed class ExchangePropertiesAttribute : Attribute
{
}
