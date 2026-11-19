namespace Caravan.Spi;

/// <summary>A properties source backed by a dictionary.</summary>
public sealed class DictionaryPropertiesSource(IDictionary<string, string?> properties) : IPropertiesSource
{
    /// <summary>Creates an empty source.</summary>
    public DictionaryPropertiesSource()
        : this(new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase))
    {
    }

    /// <summary>The properties; add to it to define placeholders.</summary>
    public IDictionary<string, string?> Properties { get; } = properties;

    /// <inheritdoc />
    public string? GetProperty(string key) => Properties.TryGetValue(key, out var value) ? value : null;
}
