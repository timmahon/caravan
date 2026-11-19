namespace Caravan.Dsl.Yaml.Catalog;

/// <summary>Factories of <see cref="OptionSpec"/>s, to keep the catalog terse.</summary>
internal static class Opt
{
    /// <summary>Common option: the node id.</summary>
    public static readonly OptionSpec Id = Str("id", "The id of the node (unique in the context).");

    /// <summary>Common option: the description.</summary>
    public static readonly OptionSpec Description = Str("description", "A description of the node.");

    /// <summary>Common option: disables the node.</summary>
    public static readonly OptionSpec Disabled = Bool("disabled", "Whether the node is disabled (skipped when the route is created).");

    /// <summary>The <c>steps</c> option.</summary>
    public static readonly OptionSpec StepsOption = Steps("steps", "The steps of the block.");

    public static OptionSpec Str(string name, string description, bool required = false) =>
        new(name, OptionKind.String, description) { Required = required };

    public static OptionSpec Ref(string name, string description, bool required = false) =>
        new(name, OptionKind.String, description + " (the name of a registry bean)") { Required = required };

    public static OptionSpec Bool(string name, string description) => new(name, OptionKind.Boolean, description);

    public static OptionSpec Int(string name, string description, bool required = false) =>
        new(name, OptionKind.Integer, description) { Required = required };

    public static OptionSpec Num(string name, string description) => new(name, OptionKind.Number, description);

    public static OptionSpec Duration(string name, string description) =>
        new(name, OptionKind.Duration, description + " (milliseconds, or a duration such as 5s, 1m30s, PT5S)");

    public static OptionSpec Enum(string name, string description, params string[] values) =>
        new(name, OptionKind.Enum, description) { EnumValues = values };

    public static OptionSpec Type(string name, string description, bool required = false) =>
        new(name, OptionKind.Type, description) { Required = required };

    public static OptionSpec Types(string name, string description, bool required = false) =>
        new(name, OptionKind.TypeList, description) { Required = required };

    public static OptionSpec Strings(string name, string description) => new(name, OptionKind.StringList, description);

    public static OptionSpec Expression(string name, string description, bool required = false) =>
        new(name, OptionKind.Expression, description) { Required = required };

    public static OptionSpec Predicate(string name, string description, bool required = false) =>
        new(name, OptionKind.Predicate, description) { Required = required };

    public static OptionSpec Steps(string name, string description) => new(name, OptionKind.Steps, description);

    public static OptionSpec Object(string name, string description, ObjectSpec nested, bool required = false) =>
        new(name, OptionKind.Object, description) { Nested = nested, Required = required };

    public static OptionSpec Objects(string name, string description, ObjectSpec nested, bool required = false) =>
        new(name, OptionKind.ObjectList, description) { Nested = nested, Required = required };

    public static OptionSpec Map(string name, string description) => new(name, OptionKind.Map, description);

    public static OptionSpec Any(string name, string description) => new(name, OptionKind.Any, description);

    /// <summary>A Camel option accepted without effect.</summary>
    public static OptionSpec Ignored(string name, OptionKind kind, string why) =>
        new(name, kind, $"Accepted for compatibility with Camel; no effect in Caravan ({why}).") { Ignored = true };

    /// <summary>A Camel option Caravan does not support: using it fails with <paramref name="why"/>.</summary>
    public static OptionSpec Unsupported(string name, string why) => new(name, OptionKind.Any, why) { Unsupported = why };

    /// <summary>Adds aliases to an option.</summary>
    public static OptionSpec Alias(this OptionSpec option, params string[] aliases) => option with { Aliases = [.. option.Aliases, .. aliases] };

    /// <summary>Marks an option required.</summary>
    public static OptionSpec Req(this OptionSpec option) => option with { Required = true };
}
