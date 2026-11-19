namespace Caravan;

/// <summary>Binds the parameter of a bean method to the value of a Simple expression, converted to the parameter type (Camel's <c>@Simple</c>).</summary>
[AttributeUsage(AttributeTargets.Parameter, Inherited = true, AllowMultiple = false)]
public sealed class SimpleAttribute(string expression) : Attribute
{
    /// <summary>The Simple expression, e.g. <c>Hello ${header.name}</c>.</summary>
    public string Expression { get; } = expression;
}
