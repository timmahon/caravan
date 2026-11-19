namespace Caravan;

/// <summary>The state of a <c>circuitBreaker</c> EIP's circuit.</summary>
public enum CircuitBreakerState
{
    /// <summary>Calls flow through; failures are counted.</summary>
    Closed,

    /// <summary>Calls are short-circuited (the fallback runs, or they fail) until the break duration has elapsed.</summary>
    Open,

    /// <summary>The break duration has elapsed: the next call is a probe that closes the circuit when it succeeds, or opens it again.</summary>
    HalfOpen,

    /// <summary>The circuit was opened manually (see <see cref="Spi.ICircuitBreakerStateRegistry.IsolateAsync"/>) and stays open until closed manually.</summary>
    Isolated,
}
