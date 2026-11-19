namespace Caravan.Health;

/// <summary>The <c>circuit-breakers</c> check: Degraded while a circuit is open or isolated, else Up.</summary>
internal sealed class CircuitBreakersHealthCheck(ICaravanContext context) : IHealthCheck
{
    public string Id => "circuit-breakers";

    public string Group => "circuit-breakers";

    public ValueTask<HealthCheckResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        var registry = context.GetCircuitBreakerStateRegistry();
        var details = new Dictionary<string, object?>();
        var open = new List<string>();
        foreach (var id in registry.Ids.Order(StringComparer.Ordinal))
        {
            if (registry.GetState(id) is not { } state)
            {
                continue;
            }

            details[id] = state.ToString();
            if (state is CircuitBreakerState.Open or CircuitBreakerState.Isolated)
            {
                open.Add(id);
            }
        }

        return ValueTask.FromResult(new HealthCheckResult(Id, open.Count == 0 ? HealthState.Up : HealthState.Degraded)
        {
            Message = open.Count == 0 ? null : "Open circuit breakers: " + string.Join(", ", open),
            Details = details,
        });
    }
}
