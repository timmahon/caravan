using Caravan.Dsl.Yaml;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Caravan.Hosting;

/// <summary>Loads YAML DSL routes (Caravan.Dsl.Yaml) into the hosted context.</summary>
public static class CaravanBuilderYamlExtensions
{
    /// <summary>
    /// Loads the YAML DSL files matching <paramref name="pattern"/> (a file, a directory or a glob such as <c>routes/*.yaml</c>, relative to the
    /// host's content root) when the context is created, before the route builders added with <c>AddRoutes</c>.
    /// </summary>
    /// <param name="builder">The Caravan builder.</param>
    /// <param name="pattern">The files to load.</param>
    /// <param name="configure">Configures the loader, e.g. <c>o =&gt; o.Watch = true</c> to reload changed files.</param>
    /// <returns>The builder.</returns>
    public static ICaravanBuilder AddYamlRoutes(this ICaravanBuilder builder, string pattern, Action<RoutesLoaderOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(pattern);
        var options = new RoutesLoaderOptions();
        configure?.Invoke(options);
        return builder.Configure((services, context) =>
        {
            var loader = new RoutesLoader(context, options)
            {
                BaseDirectory = services.GetService<IHostEnvironment>()?.ContentRootPath ?? Directory.GetCurrentDirectory(),
            };
            loader.Load(pattern);
        });
    }
}
