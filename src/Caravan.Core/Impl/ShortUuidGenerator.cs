using System.Globalization;
using Caravan.Spi;

namespace Caravan.Impl;

/// <summary>
/// An <see cref="IUuidGenerator"/> producing short ids (Camel's <c>ShortUuidGenerator</c>): 16 hexadecimal digits, a random prefix chosen
/// when the generator is created followed by a counter. Cheaper than a <see cref="Guid"/>; unique within the process for 2^32 ids per generator.
/// </summary>
public sealed class ShortUuidGenerator : IUuidGenerator
{
    private readonly string _prefix = Random.Shared.Next().ToString("x8", CultureInfo.InvariantCulture);
    private int _counter;

    /// <inheritdoc />
    public string GenerateUuid() =>
        _prefix + unchecked((uint)Interlocked.Increment(ref _counter)).ToString("x8", CultureInfo.InvariantCulture);
}
