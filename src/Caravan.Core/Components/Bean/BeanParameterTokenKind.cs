namespace Caravan.Components.Bean;

/// <summary>What a parameter of a method specification (<c>method=foo(...)</c>) is.</summary>
internal enum BeanParameterTokenKind
{
    /// <summary>A parameter type, used to choose the overload (<c>foo(string, int)</c>).</summary>
    Type,

    /// <summary><c>*</c>: any parameter type.</summary>
    AnyType,

    /// <summary>A Simple expression (<c>${body}</c>), evaluated per call.</summary>
    Simple,

    /// <summary>A constant: quoted text, a number, <c>true</c>/<c>false</c>, <c>null</c> or other text.</summary>
    Literal,
}
