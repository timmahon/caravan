namespace Caravan.Dsl.Yaml.Catalog;

/// <summary>Whether an object of the YAML DSL holds an expression inline (<c>setBody: { simple: "..." }</c>).</summary>
internal enum ExpressionSlot
{
    /// <summary>No inline expression.</summary>
    None,

    /// <summary>An inline expression.</summary>
    Expression,

    /// <summary>An inline predicate.</summary>
    Predicate,
}
