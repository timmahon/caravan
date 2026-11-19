namespace Caravan.Language.Simple;

/// <summary>The binary operators of Simple predicates.</summary>
internal enum BinaryOperator
{
    /// <summary><c>==</c>.</summary>
    Equal,

    /// <summary><c>=~</c>.</summary>
    EqualIgnoreCase,

    /// <summary><c>!=</c>.</summary>
    NotEqual,

    /// <summary><c>!=~</c>.</summary>
    NotEqualIgnoreCase,

    /// <summary><c>&gt;</c>.</summary>
    GreaterThan,

    /// <summary><c>&gt;=</c>.</summary>
    GreaterThanOrEqual,

    /// <summary><c>&lt;</c>.</summary>
    LessThan,

    /// <summary><c>&lt;=</c>.</summary>
    LessThanOrEqual,

    /// <summary><c>contains</c>.</summary>
    Contains,

    /// <summary><c>!contains</c>.</summary>
    NotContains,

    /// <summary><c>~~</c>.</summary>
    ContainsIgnoreCase,

    /// <summary><c>!~~</c>.</summary>
    NotContainsIgnoreCase,

    /// <summary><c>regex</c>.</summary>
    Regex,

    /// <summary><c>!regex</c>.</summary>
    NotRegex,

    /// <summary><c>in</c>.</summary>
    In,

    /// <summary><c>!in</c>.</summary>
    NotIn,

    /// <summary><c>range</c>.</summary>
    Range,

    /// <summary><c>!range</c>.</summary>
    NotRange,

    /// <summary><c>is</c>.</summary>
    Is,

    /// <summary><c>!is</c>.</summary>
    NotIs,

    /// <summary><c>startsWith</c>.</summary>
    StartsWith,

    /// <summary><c>!startsWith</c>.</summary>
    NotStartsWith,

    /// <summary><c>endsWith</c>.</summary>
    EndsWith,

    /// <summary><c>!endsWith</c>.</summary>
    NotEndsWith,
}
