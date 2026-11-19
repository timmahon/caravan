namespace Caravan;

/// <summary>Binds the parameter of a bean method to the exchange's exception (or the caught exception), or the first one of the parameter type in its cause chain; null when there is none (Camel's <c>@ExchangeException</c>).</summary>
[AttributeUsage(AttributeTargets.Parameter, Inherited = true, AllowMultiple = false)]
public sealed class ExchangeExceptionAttribute : Attribute
{
}
