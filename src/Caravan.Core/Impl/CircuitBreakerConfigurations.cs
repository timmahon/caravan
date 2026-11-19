using System.Collections.Concurrent;
using Caravan.Model;

namespace Caravan.Impl;

/// <summary>The default and named <see cref="CircuitBreakerConfiguration"/>s of a context (a context extension).</summary>
internal sealed class CircuitBreakerConfigurations
{
    public CircuitBreakerConfiguration? Default { get; set; }

    public ConcurrentDictionary<string, CircuitBreakerConfiguration> Named { get; } = new(StringComparer.Ordinal);
}
