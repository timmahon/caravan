namespace Caravan.Spi;

/// <summary>Resolves property placeholder values.</summary>
public interface IPropertiesSource
{
    /// <summary>Gets the value of <paramref name="key"/>, or null when this source does not define it.</summary>
    string? GetProperty(string key);
}
