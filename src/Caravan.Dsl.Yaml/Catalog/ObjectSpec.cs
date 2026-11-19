namespace Caravan.Dsl.Yaml.Catalog;

/// <summary>The options of an object of the YAML DSL (a step, a route, a nested configuration...).</summary>
internal class ObjectSpec
{
    private Dictionary<string, OptionSpec>? _byName;

    /// <summary>The options.</summary>
    public IReadOnlyList<OptionSpec> Options { get; init; } = [];

    /// <summary>Whether the object holds an inline expression or predicate.</summary>
    public ExpressionSlot Expression { get; init; }

    /// <summary>Whether the inline expression must be given.</summary>
    public bool ExpressionRequired { get; init; } = true;

    /// <summary>The option a scalar value sets (<c>to: "mock:a"</c> sets <c>uri</c>), or null.</summary>
    public string? InlineOption { get; init; }

    /// <summary>Whether the object takes the options every step has: <c>id</c>, <c>description</c>, <c>disabled</c>.</summary>
    public bool CommonOptions { get; init; }

    /// <summary>
    /// Accepts keys that are not options when this returns true for them (e.g. data formats registered in the context); they are read with
    /// <see cref="YamlObject.DynamicEntries"/>.
    /// </summary>
    public Func<ICaravanContext, string, bool>? DynamicOptions { get; init; }

    /// <summary>The option named <paramref name="name"/> (or one of its aliases), or null.</summary>
    public OptionSpec? Find(string name)
    {
        _byName ??= Options
            .SelectMany(o => o.Aliases.Prepend(o.Name).Select(n => (n, o)))
            .ToDictionary(p => p.n, p => p.o, StringComparer.Ordinal);
        return _byName.TryGetValue(name, out var option) ? option : null;
    }
}
