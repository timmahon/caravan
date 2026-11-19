using Caravan;
using Caravan.AspNetCore;
using Caravan.AspNetCore.OpenApi;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.AspNetCore.Builder;

/// <summary>Maps Caravan's HTTP endpoints into an ASP.NET Core application.</summary>
public static class CaravanEndpointRouteBuilderExtensions
{
    /// <summary>
    /// Maps the <c>platform-http</c> consumers and REST services of the hosted context (added and removed as routes start and stop), the
    /// OpenAPI document (when the REST configuration has an <c>apiContextPath</c>), the health endpoints and, optionally, the management API.
    /// Requires <c>services.AddCaravan().AddAspNetCore()</c>.
    /// </summary>
    /// <param name="endpoints">The application.</param>
    /// <param name="configure">Chooses what is mapped.</param>
    /// <returns>A builder applying conventions (e.g. <c>RequireAuthorization()</c>) to every endpoint mapped.</returns>
    public static IEndpointConventionBuilder MapCaravan(this IEndpointRouteBuilder endpoints, Action<CaravanEndpointOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        var options = new CaravanEndpointOptions();
        configure?.Invoke(options);
        var services = endpoints.ServiceProvider;
        var engine = services.GetService<PlatformHttpEngine>()
            ?? throw new InvalidOperationException("Caravan.AspNetCore is not registered: call services.AddCaravan().AddAspNetCore()");
        var context = services.GetRequiredService<ICaravanContext>();
        var builders = new List<IEndpointConventionBuilder> { endpoints.MapPlatformHttp(engine) };
        var configuration = context.GetRestConfiguration();
        var openApiPath = options.OpenApiPath
            ?? (configuration.ApiContextPath is { } api ? PlatformHttpComponent.CombinePaths(configuration.ContextPath, api) : null);
        if (openApiPath is not null)
        {
            builders.Add(endpoints.MapGet(openApiPath, () => Results.Text(OpenApiGenerator.GenerateJson(context), "application/json"))
                .WithDisplayName("Caravan OpenAPI"));
        }

        if (options.Health)
        {
            builders.AddRange(CaravanHttpEndpoints.MapHealth(endpoints, options.HealthPath, context));
        }

        if (options.Management)
        {
            builders.AddRange(CaravanHttpEndpoints.MapManagement(endpoints, options.ManagementPath, context));
        }

        return new CompositeEndpointConventionBuilder(builders);
    }

    /// <summary>Maps the consumers of a <c>platform-http</c> engine (done by <see cref="MapCaravan"/>; use it for a standalone context).</summary>
    /// <param name="endpoints">The application.</param>
    /// <param name="engine">The engine, e.g. <c>component.Engine</c>.</param>
    /// <returns>A builder applying conventions to the consumers' endpoints.</returns>
    public static IEndpointConventionBuilder MapPlatformHttp(this IEndpointRouteBuilder endpoints, PlatformHttpEngine engine)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(engine);
        if (!endpoints.DataSources.Contains(engine.EndpointDataSource))
        {
            endpoints.DataSources.Add(engine.EndpointDataSource);
        }

        return engine.Conventions;
    }
}
