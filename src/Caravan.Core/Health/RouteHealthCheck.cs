using Caravan.Impl;
using Caravan.Spi;

namespace Caravan.Health;

/// <summary>
/// The check of one route (Camel's <c>RouteHealthCheck</c>): Up when started (or intentionally not started); Down when it failed (start
/// failure, or the supervising route controller gave up); Unknown while the supervising route controller retries; Degraded when it is
/// stopped or suspended while the context runs and the route should run (auto startup).
/// </summary>
internal sealed class RouteHealthCheck(ICaravanContext context, string routeId) : IHealthCheck
{
    public string Id => "route:" + routeId;

    public string Group => "routes";

    public ValueTask<HealthCheckResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        var route = context.GetRoute(routeId);
        if (route is null)
        {
            return ValueTask.FromResult(new HealthCheckResult(Id, HealthState.Unknown) { Message = $"Route {routeId} does not exist" });
        }

        var status = route.Status;
        var details = new Dictionary<string, object?>
        {
            ["route.id"] = routeId,
            ["route.status"] = status.ToString(),
            ["route.inflight"] = route.InflightCount,
        };
        if (route is Route r)
        {
            details["route.exchanges.total"] = r.ExchangesTotal;
            details["route.exchanges.failed"] = r.ExchangesFailed;
        }

        var restart = context.SupervisingRouteController?.GetRestartState(routeId);
        HealthState state;
        string? message = null;
        switch (status)
        {
            case ServiceStatus.Started:
                state = HealthState.Up;
                break;
            case ServiceStatus.Failed when restart is { IsExhausted: false }:
                state = HealthState.Unknown;
                message = $"Route {routeId} failed to start and is being restarted (attempt {restart.Attempts})";
                break;
            case ServiceStatus.Failed:
                state = HealthState.Down;
                message = $"Route {routeId} failed" + (restart?.LastError is { } error ? ": " + error.Message : string.Empty);
                break;
            case ServiceStatus.Starting or ServiceStatus.Stopping or ServiceStatus.Suspending:
                state = HealthState.Unknown;
                message = $"Route {routeId} is {status}";
                break;
            default:
                var expected = context.Status == ServiceStatus.Started && context.AutoStartup && route.Definition.AutoStartup;
                state = expected ? HealthState.Degraded : HealthState.Up;
                message = $"Route {routeId} is {status}";
                break;
        }

        return ValueTask.FromResult(new HealthCheckResult(Id, state) { Message = message, Error = restart?.LastError, Details = details });
    }
}
