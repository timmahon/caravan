using Caravan.Spi;

namespace Caravan.Impl;

/// <summary>An <see cref="IUuidGenerator"/> producing random <see cref="Guid"/>s in the standard 36-character form (Camel's <c>RandomUuidGenerator</c>).</summary>
public sealed class RandomUuidGenerator : IUuidGenerator
{
    /// <inheritdoc />
    public string GenerateUuid() => Guid.NewGuid().ToString("D");
}
