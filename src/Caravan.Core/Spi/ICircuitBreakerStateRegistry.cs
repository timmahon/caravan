namespace Caravan.Spi;

/// <summary>
/// The circuits of the <c>circuitBreaker</c> EIPs of a context, by node id, for management (Camel exposes them over JMX). Get it with
/// <see cref="CircuitBreakerContextExtensions.GetCircuitBreakerStateRegistry"/>. The circuit of a breaker's fallback run
/// <c>onFallbackViaNetwork</c> is listed under <c>&lt;id&gt;-fallback</c>.
/// </summary>
public interface ICircuitBreakerStateRegistry
{
    /// <summary>The ids of the known circuits.</summary>
    IReadOnlyCollection<string> Ids { get; }

    /// <summary>The state of the circuit <paramref name="id"/>, or null when there is no such circuit.</summary>
    /// <param name="id">The node id of the <c>circuitBreaker</c>.</param>
    /// <returns>The state, or null.</returns>
    CircuitBreakerState? GetState(string id);

    /// <summary>Opens the circuit <paramref name="id"/> until <see cref="CloseAsync"/> is called (Resilience4j's forced open state).</summary>
    /// <param name="id">The node id of the <c>circuitBreaker</c>.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <exception cref="ArgumentException">There is no such circuit.</exception>
    Task IsolateAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>Closes the circuit <paramref name="id"/> and resets its statistics (also ends an isolation).</summary>
    /// <param name="id">The node id of the <c>circuitBreaker</c>.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <exception cref="ArgumentException">There is no such circuit.</exception>
    Task CloseAsync(string id, CancellationToken cancellationToken = default);
}
