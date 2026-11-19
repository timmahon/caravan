using Caravan.Spi;

namespace Caravan.Health;

/// <summary>
/// The check of a route's consumer (Camel's <c>ConsumerHealthCheck</c>): Down when the supervising route controller exhausted its restart
/// attempts, Unknown while it retries; a consumer implementing <see cref="IHealthCheck"/> is asked itself; otherwise Up.
/// </summary>
internal sealed class ConsumerHealthCheck(ICaravanContext context, string routeId) : IHealthCheck
{
    public string Id => "consumer:" + routeId;

    public string Group => "consumers";

    public async ValueTask<HealthCheckResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        var restart = context.SupervisingRouteController?.GetRestartState(routeId);
        var details = new Dictionary<string, object?> { ["route.id"] = routeId };
        if (restart is not null)
        {
            details["restart.attempts"] = restart.Attempts;
            details["restart.exhausted"] = restart.IsExhausted;
            return new HealthCheckResult(Id, restart.IsExhausted ? HealthState.Down : HealthState.Unknown)
            {
                Message = restart.IsExhausted
                    ? $"Consumer of route {routeId} failed to start after {restart.Attempts} restart attempt(s)"
                    : $"Consumer of route {routeId} is being restarted (attempt {restart.Attempts})",
                Error = restart.LastError,
                Details = details,
            };
        }

        var route = context.GetRoute(routeId);
        if (route?.Consumer is IHealthCheck consumerCheck)
        {
            var result = await consumerCheck.CheckAsync(cancellationToken).ConfigureAwait(false);
            return result with { Id = Id };
        }

        var state = route is { Status: ServiceStatus.Failed } ? HealthState.Down : HealthState.Up;
        return new HealthCheckResult(Id, state) { Details = details };
    }
}
