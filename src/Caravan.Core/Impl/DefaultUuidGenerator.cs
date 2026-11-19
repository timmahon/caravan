using Caravan.Spi;

namespace Caravan.Impl;

/// <summary>The default <see cref="IUuidGenerator"/>: a random <see cref="Guid"/> as 32 lower-case hexadecimal digits (<c>"N"</c> format).</summary>
public sealed class DefaultUuidGenerator : IUuidGenerator
{
    /// <summary>The shared instance.</summary>
    public static DefaultUuidGenerator Instance { get; } = new();

    /// <inheritdoc />
    public string GenerateUuid() => Guid.NewGuid().ToString("N");
}
