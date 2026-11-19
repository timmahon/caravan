using Caravan.Spi;

namespace Caravan.Health;

/// <summary>The <c>context</c> check (liveness and readiness): Up while the context is started (Camel's <c>ContextHealthCheck</c>).</summary>
internal sealed class ContextHealthCheck(ICaravanContext context) : IHealthCheck
{
    public string Id => "context";

    public string Group => "context";

    public bool IsLiveness => true;

    public ValueTask<HealthCheckResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        var status = context.Status;
        var state = status switch
        {
            ServiceStatus.Started => HealthState.Up,
            ServiceStatus.Starting => HealthState.Unknown,
            _ => HealthState.Down,
        };
        return ValueTask.FromResult(new HealthCheckResult(Id, state)
        {
            Message = state == HealthState.Up ? null : $"Context {context.Name} is {status}",
            Details = new Dictionary<string, object?> { ["context.name"] = context.Name, ["context.status"] = status.ToString() },
        });
    }
}
