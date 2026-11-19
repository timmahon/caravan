using Caravan;
using Caravan.Health;
using Caravan.Hosting;
using Caravan.Management;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Registers Caravan in a service collection.</summary>
public static class CaravanServiceCollectionExtensions
{
    /// <summary>
    /// Registers a Caravan context run by the host (Camel main / Spring Boot's auto-configuration): the context as <see cref="ICaravanContext"/>
    /// and <see cref="CaravanContext"/> singletons, created on first use with a registry backed by the service provider, the host's
    /// <c>ILoggerFactory</c>, <see cref="TimeProvider"/> (when registered) and <c>IConfiguration</c> (as a properties source); singleton
    /// <see cref="IProducerTemplate"/> and <see cref="IConsumerTemplate"/> (thread-safe, meant to be reused, as in Camel), the
    /// <see cref="IManagementService"/> and <see cref="HealthCheckRegistry"/>; and the <see cref="CaravanHostedService"/> starting and stopping
    /// it with the host. Options are bound from the <c>Caravan</c> configuration section, then <paramref name="configure"/> runs. Calling it again
    /// returns a builder over the same registration.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Configures the options after binding.</param>
    /// <returns>A builder to add routes, components and configuration.</returns>
    public static ICaravanBuilder AddCaravan(this IServiceCollection services, Action<CaravanOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        var optionsBuilder = services.AddOptions<CaravanOptions>();
        var existing = services.FirstOrDefault(d => d.ServiceType == typeof(CaravanSetup))?.ImplementationInstance as CaravanSetup;
        if (existing is null)
        {
            optionsBuilder.BindConfiguration(CaravanOptions.SectionName);
        }

        if (configure is not null)
        {
            optionsBuilder.Configure(configure);
        }

        if (existing is not null)
        {
            return new CaravanBuilder(services, existing);
        }

        var setup = new CaravanSetup();
        services.AddSingleton(setup);
        services.TryAddSingleton(CaravanContextFactory.Create);
        services.TryAddSingleton<ICaravanContext>(sp => sp.GetRequiredService<CaravanContext>());
        services.TryAddSingleton(sp => sp.GetRequiredService<ICaravanContext>().CreateProducerTemplate());
        services.TryAddSingleton(sp => sp.GetRequiredService<ICaravanContext>().CreateConsumerTemplate());
        services.TryAddSingleton(sp => sp.GetRequiredService<ICaravanContext>().Management);
        services.TryAddSingleton(sp => sp.GetRequiredService<ICaravanContext>().HealthChecks);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, CaravanHostedService>());
        return new CaravanBuilder(services, setup);
    }
}
