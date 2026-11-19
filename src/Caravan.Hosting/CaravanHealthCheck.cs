using Caravan.Health;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using CaravanCheck = Caravan.Health.IHealthCheck;
using MsHealthCheck = Microsoft.Extensions.Diagnostics.HealthChecks.IHealthCheck;
using MsHealthCheckResult = Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult;

namespace Caravan.Hosting;

/// <summary>
/// Exposes a group of Caravan health checks (<see cref="HealthCheckRegistry"/>) as one ASP.NET Core / Generic Host health check: Down →
/// the registration's failure status (Unhealthy by default), Degraded or Unknown → Degraded, else Healthy. The data holds each check's state
/// and message.
/// </summary>
internal sealed class CaravanHealthCheck(HealthCheckRegistry registry, Func<CaravanCheck, bool> filter) : MsHealthCheck
{
    public async Task<MsHealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var results = await registry.CheckAsync(filter, cancellationToken).ConfigureAwait(false);
        var data = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var result in results)
        {
            data[result.Id] = result.Message is null ? result.State.ToString() : $"{result.State}: {result.Message}";
        }

        var problems = results.Where(r => r.State != HealthState.Up).ToList();
        var description = problems.Count == 0 ? null : string.Join("; ", problems.Select(r => r.Message ?? $"{r.Id} is {r.State}"));
        var error = problems.Select(r => r.Error).FirstOrDefault(e => e is not null);
        return HealthCheckRegistry.Aggregate(results) switch
        {
            HealthState.Down => new MsHealthCheckResult(context.Registration.FailureStatus, description, error, data),
            HealthState.Up => MsHealthCheckResult.Healthy(description, data),
            _ => MsHealthCheckResult.Degraded(description, error, data),
        };
    }
}
