using Caravan;
using Caravan.Hosting;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using CaravanCheck = Caravan.Health.IHealthCheck;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Adds Caravan's health checks to the host's health checks.</summary>
public static class CaravanHealthChecksBuilderExtensions
{
    private static readonly string[] BuiltInGroups = ["context", "routes", "consumers", "circuit-breakers"];

    /// <summary>
    /// Adds Caravan's health checks (from <see cref="ICaravanContext.HealthChecks"/>), one host check per group: <c>caravan-context</c>
    /// (liveness and readiness: the context is started), <c>caravan-routes</c> (a route failed → Unhealthy, stopped unexpectedly → Degraded),
    /// <c>caravan-consumers</c> (the supervising route controller gave up on a route → Unhealthy), <c>caravan-circuit-breakers</c> (a circuit is
    /// open → Degraded) and <c>caravan-components</c> (checks contributed by components, beans and registered repositories, e.g. a full
    /// <c>seda</c> queue → Degraded); all but the context check are readiness checks. Requires <c>AddCaravan</c>.
    /// </summary>
    /// <param name="builder">The health checks builder.</param>
    /// <param name="configure">Configures names and tags.</param>
    /// <returns>The builder.</returns>
    public static IHealthChecksBuilder AddCaravanHealthChecks(this IHealthChecksBuilder builder, Action<CaravanHealthCheckOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var options = new CaravanHealthCheckOptions();
        configure?.Invoke(options);
        Add(builder, options, "context", c => c.Group == "context", liveness: true);
        Add(builder, options, "routes", c => c.Group == "routes", liveness: false);
        Add(builder, options, "consumers", c => c.Group == "consumers", liveness: false);
        Add(builder, options, "circuit-breakers", c => c.Group == "circuit-breakers", liveness: false);
        Add(builder, options, "components", c => !BuiltInGroups.Contains(c.Group), liveness: false);
        return builder;
    }

    private static void Add(IHealthChecksBuilder builder, CaravanHealthCheckOptions options, string group, Func<CaravanCheck, bool> filter, bool liveness)
    {
        var tags = new List<string>(options.Tags) { options.ReadinessTag };
        if (liveness)
        {
            tags.Add(options.LivenessTag);
        }

        builder.Add(new HealthCheckRegistration(
            $"{options.NamePrefix}-{group}",
            sp => new CaravanHealthCheck(sp.GetRequiredService<ICaravanContext>().HealthChecks, filter),
            failureStatus: null,
            tags,
            options.Timeout));
    }
}
