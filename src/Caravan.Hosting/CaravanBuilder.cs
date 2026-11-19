using Caravan.Builder;
using Caravan.Spi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Caravan.Hosting;

/// <summary>The <see cref="ICaravanBuilder"/> returned by <c>AddCaravan</c>.</summary>
internal sealed class CaravanBuilder(IServiceCollection services, CaravanSetup setup) : ICaravanBuilder
{
    public IServiceCollection Services { get; } = services;

    public ICaravanBuilder AddRoutes<TRouteBuilder>()
        where TRouteBuilder : class, IRoutesBuilder
    {
        Services.TryAddSingleton<TRouteBuilder>();
        setup.Routes.Add(sp => sp.GetRequiredService<TRouteBuilder>());
        return this;
    }

    public ICaravanBuilder AddRoutes(IRoutesBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);
        setup.Routes.Add(_ => routes);
        return this;
    }

    public ICaravanBuilder AddRoutes(Func<IServiceProvider, IRoutesBuilder> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        setup.Routes.Add(factory);
        return this;
    }

    public ICaravanBuilder AddRouteConfiguration<TConfiguration>()
        where TConfiguration : RouteConfigurationBuilder => AddRoutes<TConfiguration>();

    public ICaravanBuilder AddComponent(string scheme, Func<IServiceProvider, IComponent> factory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scheme);
        ArgumentNullException.ThrowIfNull(factory);
        setup.Components.Add((scheme, factory));
        return this;
    }

    public ICaravanBuilder AddRouteTemplateParameterSource(IRouteTemplateParameterSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        setup.ParameterSources.Add(_ => source);
        return this;
    }

    public ICaravanBuilder AddRouteTemplateParameterSource<TSource>()
        where TSource : class, IRouteTemplateParameterSource
    {
        Services.TryAddSingleton<TSource>();
        setup.ParameterSources.Add(sp => sp.GetRequiredService<TSource>());
        return this;
    }

    public ICaravanBuilder Configure(Action<ICaravanContext> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        setup.Configurers.Add((_, context) => configure(context));
        return this;
    }

    public ICaravanBuilder Configure(Action<IServiceProvider, ICaravanContext> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        setup.Configurers.Add(configure);
        return this;
    }
}
