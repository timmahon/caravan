using Caravan.Builder;
using Caravan.Spi;
using Microsoft.Extensions.DependencyInjection;

namespace Caravan.Hosting;

/// <summary>Configures the Caravan context registered by <c>services.AddCaravan()</c>.</summary>
public interface ICaravanBuilder
{
    /// <summary>The service collection.</summary>
    IServiceCollection Services { get; }

    /// <summary>
    /// Adds the routes of a route builder resolved from the service provider (registered as a singleton when it is not registered yet), so its
    /// constructor can take services.
    /// </summary>
    /// <typeparam name="TRouteBuilder">The route builder type.</typeparam>
    /// <returns>This builder.</returns>
    ICaravanBuilder AddRoutes<TRouteBuilder>()
        where TRouteBuilder : class, IRoutesBuilder;

    /// <summary>Adds the routes of a route builder instance.</summary>
    /// <param name="routes">The route builder.</param>
    /// <returns>This builder.</returns>
    ICaravanBuilder AddRoutes(IRoutesBuilder routes);

    /// <summary>Adds the routes of a route builder created from the service provider.</summary>
    /// <param name="factory">Creates the route builder.</param>
    /// <returns>This builder.</returns>
    ICaravanBuilder AddRoutes(Func<IServiceProvider, IRoutesBuilder> factory);

    /// <summary>Adds the route configurations of a <see cref="RouteConfigurationBuilder"/> resolved from the service provider.</summary>
    /// <typeparam name="TConfiguration">The route configuration builder type.</typeparam>
    /// <returns>This builder.</returns>
    ICaravanBuilder AddRouteConfiguration<TConfiguration>()
        where TConfiguration : RouteConfigurationBuilder;

    /// <summary>Registers a component for <paramref name="scheme"/>, created from the service provider when the context is created.</summary>
    /// <param name="scheme">The URI scheme.</param>
    /// <param name="factory">Creates the component.</param>
    /// <returns>This builder.</returns>
    ICaravanBuilder AddComponent(string scheme, Func<IServiceProvider, IComponent> factory);

    /// <summary>Adds a source of templated routes created when the context starts.</summary>
    /// <param name="source">The source.</param>
    /// <returns>This builder.</returns>
    ICaravanBuilder AddRouteTemplateParameterSource(IRouteTemplateParameterSource source);

    /// <summary>Adds a source of templated routes resolved from the service provider (registered as a singleton when it is not registered yet).</summary>
    /// <typeparam name="TSource">The source type.</typeparam>
    /// <returns>This builder.</returns>
    ICaravanBuilder AddRouteTemplateParameterSource<TSource>()
        where TSource : class, IRouteTemplateParameterSource;

    /// <summary>Configures the context when it is created (after the options are applied, before routes are added).</summary>
    /// <param name="configure">The configuration.</param>
    /// <returns>This builder.</returns>
    ICaravanBuilder Configure(Action<ICaravanContext> configure);

    /// <summary>Configures the context with access to the service provider.</summary>
    /// <param name="configure">The configuration.</param>
    /// <returns>This builder.</returns>
    ICaravanBuilder Configure(Action<IServiceProvider, ICaravanContext> configure);
}
