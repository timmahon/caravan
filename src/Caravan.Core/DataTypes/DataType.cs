namespace Caravan.DataTypes;

/// <summary>
/// A data type (Camel's <c>DataType</c>): a <see cref="Scheme"/> (<c>json</c>, <c>xml</c>, <c>clr</c> for .NET types — <c>java</c> is accepted as
/// an alias) and an optional <see cref="Name"/>, written <c>scheme:name</c> (<c>json:Order</c>, <c>clr:MyApp.Order</c>). A type without name
/// (<c>json</c>) stands for every name of its scheme when transformers are matched.
/// </summary>
public sealed record DataType
{
    /// <summary>The scheme used for .NET types.</summary>
    public const string ClrScheme = "clr";

    /// <summary>Creates a data type.</summary>
    /// <param name="scheme">The scheme.</param>
    /// <param name="name">The name, or null.</param>
    public DataType(string scheme, string? name = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scheme);
        Scheme = scheme;
        Name = string.IsNullOrEmpty(name) ? null : name;
    }

    /// <summary>The scheme (<c>json</c>, <c>xml</c>, <c>clr</c>, ...).</summary>
    public string Scheme { get; }

    /// <summary>The name inside the scheme, or null.</summary>
    public string? Name { get; }

    /// <summary>Whether this is a .NET type (<c>clr:</c> or Camel's <c>java:</c> scheme with a name).</summary>
    public bool IsClrType => Name is not null && (Scheme == ClrScheme || Scheme == "java");

    /// <summary>Parses <c>scheme:name</c> or <c>scheme</c>.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The data type.</returns>
    public static DataType Parse(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        var colon = text.IndexOf(':', StringComparison.Ordinal);
        return colon < 0 ? new DataType(text.Trim()) : new DataType(text[..colon].Trim(), text[(colon + 1)..].Trim());
    }

    /// <summary>The data type of a .NET type (<c>clr:Full.Name</c>).</summary>
    /// <param name="type">The type.</param>
    /// <returns>The data type.</returns>
    public static DataType FromType(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return new DataType(ClrScheme, type.FullName ?? type.Name);
    }

    /// <summary>The data type of <typeparamref name="T"/> (<c>clr:Full.Name</c>).</summary>
    /// <typeparam name="T">The type.</typeparam>
    /// <returns>The data type.</returns>
    public static DataType Of<T>() => FromType(typeof(T));

    /// <summary>Whether <paramref name="other"/> is this type, or of this type's scheme when this type has no name.</summary>
    /// <param name="other">The type to test.</param>
    /// <returns>True when it matches.</returns>
    public bool Matches(DataType other) =>
        string.Equals(Scheme, other.Scheme, StringComparison.Ordinal) && (Name is null || string.Equals(Name, other.Name, StringComparison.Ordinal));

    /// <inheritdoc />
    public override string ToString() => Name is null ? Scheme : Scheme + ":" + Name;
}
