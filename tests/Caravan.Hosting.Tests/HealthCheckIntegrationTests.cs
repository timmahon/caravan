using Caravan.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;

namespace Caravan.Hosting.Tests;

public class HealthCheckIntegrationTests
{
    private static IHost Build()
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        builder.Services.AddCaravan().AddRoutes(RouteBuilder.Create(b =>
        {
            b.From("direct:a").RouteId("a").To("mock:a");
            b.From("direct:cb").RouteId("cb").CircuitBreaker(c => c.To("mock:cb")).Id("breaker");
        }));
        builder.Services.AddHealthChecks().AddCaravanHealthChecks();
        return builder.Build();
    }

    [Fact]
    public async Task Health_follows_the_context_and_routes()
    {
        using var host = Build();
        var health = host.Services.GetRequiredService<HealthCheckService>();

        var before = await health.CheckHealthAsync();
        Assert.Equal(HealthStatus.Unhealthy, before.Entries["caravan-context"].Status);

        await host.StartAsync();
        var context = host.Services.GetRequiredService<ICaravanContext>();
        var report = await health.CheckHealthAsync();
        Assert.Equal(HealthStatus.Healthy, report.Status);
        Assert.Equal(
            ["caravan-circuit-breakers", "caravan-components", "caravan-consumers", "caravan-context", "caravan-routes"],
            report.Entries.Keys.Order(StringComparer.Ordinal));
        Assert.Equal("Up", report.Entries["caravan-routes"].Data["route:a"]);

        var live = await health.CheckHealthAsync(r => r.Tags.Contains("live"));
        Assert.Equal(["caravan-context"], live.Entries.Keys);
        Assert.Equal(5, (await health.CheckHealthAsync(r => r.Tags.Contains("ready"))).Entries.Count);

        await context.StopRouteAsync("a");
        report = await health.CheckHealthAsync();
        Assert.Equal(HealthStatus.Degraded, report.Entries["caravan-routes"].Status);
        Assert.Contains("Route a is Stopped", report.Entries["caravan-routes"].Description, StringComparison.Ordinal);

        await host.Services.GetRequiredService<IProducerTemplate>().SendBodyAsync("direct:cb", 1);
        await context.GetCircuitBreakerStateRegistry().IsolateAsync("breaker");
        report = await health.CheckHealthAsync();
        Assert.Equal(HealthStatus.Degraded, report.Entries["caravan-circuit-breakers"].Status);

        await host.StopAsync();
        Assert.Equal(HealthStatus.Unhealthy, (await health.CheckHealthAsync()).Status);
    }
}
