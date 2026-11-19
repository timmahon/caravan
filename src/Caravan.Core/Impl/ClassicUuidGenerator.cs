using System.Globalization;
using Caravan.Spi;

namespace Caravan.Impl;

/// <summary>
/// An <see cref="IUuidGenerator"/> in the style of Camel's classic generator: <c>ID-{host}-{seed}-0-{counter}</c>, where the seed is the time
/// the generator was created (Unix milliseconds) and the counter increments. Readable and ordered, unique per host and generator.
/// </summary>
public sealed class ClassicUuidGenerator : IUuidGenerator
{
    private readonly string _prefix = "ID-" + SanitizeHost(Environment.MachineName) + "-" +
        DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture) + "-0-";

    private long _counter;

    /// <inheritdoc />
    public string GenerateUuid() => _prefix + Interlocked.Increment(ref _counter).ToString(CultureInfo.InvariantCulture);

    private static string SanitizeHost(string host) =>
        string.Concat(host.Select(c => char.IsLetterOrDigit(c) || c == '-' ? c : '-'));
}
