using Caravan.AspNetCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Caravan.Hosting;

/// <summary>Adds ASP.NET Core support (Caravan.AspNetCore) to the hosted context.</summary>
public static class CaravanAspNetCoreBuilderExtensions
{
    /// <summary>
    /// Registers the <c>platform-http</c> component (on a singleton <see cref="PlatformHttpEngine"/>) and makes it the REST DSL's component
    /// unless the REST configuration names another. Map the endpoints with <c>app.MapCaravan()</c>.
    /// </summary>
    /// <param name="builder">The Caravan builder.</param>
    /// <param name="configure">Configures the component (exception mapping, <c>MuteException</c>, header filter).</param>
    /// <returns>The builder.</returns>
    public static ICaravanBuilder AddAspNetCore(this ICaravanBuilder builder, Action<PlatformHttpComponent>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.TryAddSingleton<PlatformHttpEngine>();
        builder.AddComponent(PlatformHttpComponent.Scheme, sp =>
        {
            var component = new PlatformHttpComponent(sp.GetRequiredService<PlatformHttpEngine>());
            configure?.Invoke(component);
            return component;
        });
        builder.Configure(context =>
        {
            var configuration = context.GetRestConfiguration();
            configuration.Component ??= PlatformHttpComponent.Scheme;
        });
        return builder;
    }
}
