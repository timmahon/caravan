namespace Caravan.Health;

/// <summary>The outcome of a health check (Camel's <c>HealthCheck.State</c>, plus <see cref="Degraded"/>).</summary>
public enum HealthState
{
    /// <summary>Healthy.</summary>
    Up,

    /// <summary>Working but impaired (e.g. an open circuit breaker, a full queue, a route stopped unexpectedly).</summary>
    Degraded,

    /// <summary>Not healthy.</summary>
    Down,

    /// <summary>Not known yet (e.g. a route being restarted by the supervising route controller).</summary>
    Unknown,
}
