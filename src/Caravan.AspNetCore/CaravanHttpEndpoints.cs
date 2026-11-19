using System.Text.Json;
using System.Text.Json.Serialization;
using Caravan.Health;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Caravan.AspNetCore;

/// <summary>The health and management endpoints mapped by <c>MapCaravan</c>.</summary>
internal static class CaravanHttpEndpoints
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        ReferenceHandler = ReferenceHandler.IgnoreCycles,
    };

    /// <summary>
    /// <c>GET {path}</c> (every check), <c>{path}/live</c> (liveness) and <c>{path}/ready</c> (readiness): the aggregated state and each check;
    /// 503 when the state is <see cref="HealthState.Down"/>, else 200 (Camel's health endpoint).
    /// </summary>
    public static IEnumerable<IEndpointConventionBuilder> MapHealth(IEndpointRouteBuilder endpoints, string path, ICaravanContext context)
    {
        var registry = context.HealthChecks;
        yield return endpoints.MapGet(path, (CancellationToken token) => Health(registry.CheckAsync(null, token)))
            .WithDisplayName("Caravan health");
        yield return endpoints.MapGet(path.TrimEnd('/') + "/live", (CancellationToken token) => Health(registry.CheckLivenessAsync(token)))
            .WithDisplayName("Caravan liveness");
        yield return endpoints.MapGet(path.TrimEnd('/') + "/ready", (CancellationToken token) => Health(registry.CheckReadinessAsync(token)))
            .WithDisplayName("Caravan readiness");
    }

    /// <summary>
    /// The management API (<see cref="Management.IManagementService"/>): <c>GET {path}</c> (context statistics), <c>{path}/routes</c>,
    /// <c>{path}/routes/{id}</c>, <c>{path}/routes/{id}/processors</c>, <c>{path}/routes/{id}/dump</c>, <c>{path}/endpoints</c>,
    /// <c>{path}/components</c>, <c>{path}/circuit-breakers</c>, <c>{path}/inflight</c>, and <c>POST {path}/routes/{id}/{action}</c> with
    /// <c>start</c>, <c>stop</c>, <c>suspend</c>, <c>resume</c> or <c>reset</c>.
    /// </summary>
    public static IEnumerable<IEndpointConventionBuilder> MapManagement(IEndpointRouteBuilder endpoints, string path, ICaravanContext context)
    {
        var management = context.Management;
        var root = path.TrimEnd('/');
        yield return endpoints.MapGet(root, () => Results.Json(management.GetContextStatistics(), Json)).WithDisplayName("Caravan management");
        yield return endpoints.MapGet(root + "/routes", () => Results.Json(management.GetRouteStatistics(), Json));
        yield return endpoints.MapGet(root + "/routes/{routeId}", (string routeId) =>
            management.GetRouteStatistics(routeId) is { } statistics ? Results.Json(statistics, Json) : Results.NotFound());
        yield return endpoints.MapGet(root + "/routes/{routeId}/processors", (string routeId) =>
            management.GetRouteStatistics(routeId) is null ? Results.NotFound() : Results.Json(management.GetProcessorStatistics(routeId), Json));
        yield return endpoints.MapGet(root + "/routes/{routeId}/dump", (string routeId) =>
            management.DumpRouteAsText(routeId) is { } text ? Results.Text(text) : Results.NotFound());
        yield return endpoints.MapGet(root + "/endpoints", () => Results.Json(management.ListEndpoints(), Json));
        yield return endpoints.MapGet(root + "/components", () => Results.Json(management.ListComponents(), Json));
        yield return endpoints.MapGet(root + "/circuit-breakers", () => Results.Json(management.ListCircuitBreakers(), Json));
        yield return endpoints.MapGet(root + "/inflight", (string? routeId, int? limit) =>
            Results.Json(management.BrowseInflight(routeId, limit ?? -1), Json));
        yield return endpoints.MapPost(root + "/routes/{routeId}/{action}", async (string routeId, string action, CancellationToken token) =>
        {
            if (context.GetRoute(routeId) is null)
            {
                return Results.NotFound();
            }

            switch (action.ToLowerInvariant())
            {
                case "start":
                    await management.StartRouteAsync(routeId, token).ConfigureAwait(false);
                    break;
                case "stop":
                    await management.StopRouteAsync(routeId, token).ConfigureAwait(false);
                    break;
                case "suspend":
                    await management.SuspendRouteAsync(routeId, token).ConfigureAwait(false);
                    break;
                case "resume":
                    await management.ResumeRouteAsync(routeId, token).ConfigureAwait(false);
                    break;
                case "reset":
                    management.ResetRoute(routeId);
                    break;
                default:
                    return Results.BadRequest($"Unknown action '{action}': use start, stop, suspend, resume or reset");
            }

            return Results.Json(management.GetRouteStatistics(routeId), Json);
        });
    }

    private static async Task<IResult> Health(Task<IReadOnlyList<HealthCheckResult>> check)
    {
        var results = await check.ConfigureAwait(false);
        var state = HealthCheckRegistry.Aggregate(results);
        var body = new
        {
            Status = Name(state),
            Checks = results.Select(r => new
            {
                r.Id,
                r.Group,
                Status = Name(r.State),
                r.Message,
                Error = r.Error?.Message,
                Details = r.Details.Count == 0 ? null : r.Details.ToDictionary(d => d.Key, d => d.Value?.ToString()),
            }),
        };
        return Results.Json(body, Json, statusCode: state == HealthState.Down ? StatusCodes.Status503ServiceUnavailable : StatusCodes.Status200OK);
    }

    private static string Name(HealthState state) => state.ToString().ToUpperInvariant();
}
