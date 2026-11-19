using Caravan.Hosting;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Caravan.Samples.WebApi;

/// <summary>Composes the sample application (kept apart from <c>Program</c> so tests can host it on a test server).</summary>
public static class WebApiSample
{
    /// <summary>Creates the application: Caravan with platform-http, C# and YAML routes, health checks, OpenTelemetry and the HTTP endpoints.</summary>
    /// <param name="args">The command line.</param>
    /// <param name="configure">Changes the builder before the application is built (tests use a test server).</param>
    /// <returns>The application, not started.</returns>
    public static WebApplication Create(string[] args, Action<WebApplicationBuilder>? configure = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = args, ContentRootPath = AppContext.BaseDirectory });

        builder.Services.AddSingleton<Inventory>();
        builder.Services.AddCaravan(o => o.Name = "webapi")
            .AddAspNetCore()
            .Configure((services, context) => context.Registry.Bind("inventory", services.GetRequiredService<Inventory>()))
            .AddRoutes<InventoryRoutes>()
            .AddYamlRoutes("routes/*.yaml", o => o.Watch = builder.Configuration.GetValue("Sample:WatchRoutes", false));

        builder.Services.AddHealthChecks().AddCaravanHealthChecks();

        var otel = builder.Services.AddOpenTelemetry()
            .WithTracing(t => t.AddCaravanInstrumentation())
            .WithMetrics(m => m.AddCaravanInstrumentation());
        if (builder.Configuration.GetValue("OpenTelemetry:Console", false))
        {
            otel.WithTracing(t => t.AddConsoleExporter()).WithMetrics(m => m.AddConsoleExporter());
        }

        configure?.Invoke(builder);
        var app = builder.Build();
        app.MapCaravan(o =>
        {
            o.OpenApiPath = "/openapi.json";
            o.Management = true;
        });
        app.MapHealthChecks("/health");
        return app;
    }
}
