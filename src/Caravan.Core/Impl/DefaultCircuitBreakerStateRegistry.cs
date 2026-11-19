using System.Collections.Concurrent;
using Caravan.Processors;
using Caravan.Spi;

namespace Caravan.Impl;

/// <summary>Default <see cref="ICircuitBreakerStateRegistry"/>: the circuits registered by the <c>circuitBreaker</c> processors of a context.</summary>
internal sealed class DefaultCircuitBreakerStateRegistry : ICircuitBreakerStateRegistry
{
    private readonly ConcurrentDictionary<string, CircuitBreakerCircuit> _circuits = new(StringComparer.Ordinal);

    public IReadOnlyCollection<string> Ids => [.. _circuits.Keys];

    public CircuitBreakerState? GetState(string id) => _circuits.TryGetValue(id, out var circuit) ? circuit.State : null;

    public Task IsolateAsync(string id, CancellationToken cancellationToken = default) => Get(id).IsolateAsync(cancellationToken);

    public Task CloseAsync(string id, CancellationToken cancellationToken = default) => Get(id).CloseAsync(cancellationToken);

    /// <summary>Registers (or replaces, when a route is created again) the circuit <paramref name="id"/>.</summary>
    public void Register(string id, CircuitBreakerCircuit circuit) => _circuits[id] = circuit;

    private CircuitBreakerCircuit Get(string id) =>
        _circuits.TryGetValue(id, out var circuit) ? circuit : throw new ArgumentException($"No circuit breaker with id: {id}", nameof(id));
}
