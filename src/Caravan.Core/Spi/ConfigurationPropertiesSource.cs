using Microsoft.Extensions.Configuration;

namespace Caravan.Spi;

/// <summary>
/// A properties source backed by an <see cref="IConfiguration"/>. A key is looked up as is, then with dots replaced by the
/// configuration path separator, so <c>{{app.queue}}</c> finds <c>"app": { "queue": ... }</c> in <c>appsettings.json</c> and the
/// environment variable <c>app__queue</c> read by the environment variables configuration provider.
/// </summary>
public sealed class ConfigurationPropertiesSource(IConfiguration configuration) : IPropertiesSource
{
    /// <summary>The configuration read.</summary>
    public IConfiguration Configuration { get; } = configuration ?? throw new ArgumentNullException(nameof(configuration));

    /// <inheritdoc />
    public string? GetProperty(string key)
    {
        if (Configuration[key] is { } value)
        {
            return value;
        }

        return key.Contains('.', StringComparison.Ordinal)
            ? Configuration[key.Replace('.', ConfigurationPath.KeyDelimiter[0])]
            : null;
    }
}
