namespace Caravan.Management;

/// <summary>Management view of the circuit of a <c>circuitBreaker</c> node.</summary>
/// <param name="Id">The node id (with <c>-fallback</c> for the circuit of <c>onFallbackViaNetwork</c>).</param>
/// <param name="State">The circuit state.</param>
public sealed record CircuitBreakerInfo(string Id, CircuitBreakerState State);
