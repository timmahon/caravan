using System.Globalization;
using Caravan.Spi;

namespace Caravan.Impl;

/// <summary>
/// An <see cref="IUuidGenerator"/> returning an incrementing counter (<c>1</c>, <c>2</c>, ...), unique within the generator only
/// (Camel's <c>SimpleUuidGenerator</c>). Handy in tests, where ids become predictable.
/// </summary>
public sealed class SimpleUuidGenerator : IUuidGenerator
{
    private long _counter;

    /// <inheritdoc />
    public string GenerateUuid() => Interlocked.Increment(ref _counter).ToString(CultureInfo.InvariantCulture);
}
