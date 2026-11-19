namespace Caravan.Health;

/// <summary>The result of an <see cref="IHealthCheck"/> (Camel's <c>HealthCheck.Result</c>).</summary>
/// <param name="Id">The id of the check.</param>
/// <param name="State">The state.</param>
public sealed record HealthCheckResult(string Id, HealthState State)
{
    /// <summary>A human-readable explanation, if any.</summary>
    public string? Message { get; init; }

    /// <summary>The error that made the check fail, if any.</summary>
    public Exception? Error { get; init; }

    /// <summary>Details (route status, counters, queue sizes, ...).</summary>
    public IReadOnlyDictionary<string, object?> Details { get; init; } = new Dictionary<string, object?>();

    /// <summary>The group of the check (<see cref="IHealthCheck.Group"/>).</summary>
    public string? Group { get; init; }
}
