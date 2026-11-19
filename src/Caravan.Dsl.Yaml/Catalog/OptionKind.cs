namespace Caravan.Dsl.Yaml.Catalog;

/// <summary>The kind of value an option of the YAML DSL takes (drives parsing, validation and the JSON schema).</summary>
internal enum OptionKind
{
    /// <summary>Text.</summary>
    String,

    /// <summary>A whole number.</summary>
    Integer,

    /// <summary>A decimal number.</summary>
    Number,

    /// <summary><c>true</c> or <c>false</c>.</summary>
    Boolean,

    /// <summary>A duration: milliseconds, <c>1h2m3s4ms</c>, ISO-8601 (<c>PT5S</c>) or <c>hh:mm:ss</c>.</summary>
    Duration,

    /// <summary>One of <see cref="OptionSpec.EnumValues"/> (case-insensitive).</summary>
    Enum,

    /// <summary>A .NET type name (Java names of common types and exceptions are mapped).</summary>
    Type,

    /// <summary>A list of type names (a single name is accepted too).</summary>
    TypeList,

    /// <summary>A list of strings (a single string is accepted too).</summary>
    StringList,

    /// <summary>An object holding one expression (<c>{ simple: "..." }</c>).</summary>
    Expression,

    /// <summary>An object holding one predicate (<c>{ simple: "..." }</c>), or <c>true</c>/<c>false</c>.</summary>
    Predicate,

    /// <summary>A list of steps.</summary>
    Steps,

    /// <summary>A nested object described by <see cref="OptionSpec.Nested"/>.</summary>
    Object,

    /// <summary>A list of nested objects described by <see cref="OptionSpec.Nested"/>.</summary>
    ObjectList,

    /// <summary>A free-form mapping (endpoint parameters, bean properties).</summary>
    Map,

    /// <summary>Any value.</summary>
    Any,
}
