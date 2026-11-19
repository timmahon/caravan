namespace Caravan.Language.Simple;

/// <summary>The kind of an OGNL step.</summary>
internal enum OgnlStepKind
{
    /// <summary><c>.Name</c>: a property, field or dictionary key.</summary>
    Property,

    /// <summary><c>.Name(args)</c>: a method call.</summary>
    Method,

    /// <summary><c>[key]</c>: a list/array index (<c>last</c>, <c>last-1</c>) or a dictionary key.</summary>
    Index,
}
