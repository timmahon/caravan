namespace Caravan.Dsl.Yaml.Catalog;

/// <summary>An option of a step or another object of the YAML DSL.</summary>
/// <param name="Name">The camelCase name (kebab-case is accepted too).</param>
/// <param name="Kind">The kind of value.</param>
/// <param name="Description">What it does (goes into the JSON schema).</param>
internal sealed record OptionSpec(string Name, OptionKind Kind, string Description)
{
    /// <summary>Whether the option must be given.</summary>
    public bool Required { get; init; }

    /// <summary>Other accepted names (e.g. Camel 3 names).</summary>
    public IReadOnlyList<string> Aliases { get; init; } = [];

    /// <summary>The values of an <see cref="OptionKind.Enum"/> option.</summary>
    public IReadOnlyList<string>? EnumValues { get; init; }

    /// <summary>The nested object of an <see cref="OptionKind.Object"/> or <see cref="OptionKind.ObjectList"/> option.</summary>
    public ObjectSpec? Nested { get; init; }

    /// <summary>
    /// For an <see cref="OptionKind.ObjectList"/>: a key the items may be wrapped in (Camel writes
    /// <c>onException: [ { onException: { ... } } ]</c> in route configurations).
    /// </summary>
    public string? ItemWrapper { get; init; }

    /// <summary>Accepted for compatibility with Camel but without effect in Caravan.</summary>
    public bool Ignored { get; init; }

    /// <summary>When set, using the option fails with this reason (a Camel option Caravan cannot honour); not in the schema.</summary>
    public string? Unsupported { get; init; }
}
