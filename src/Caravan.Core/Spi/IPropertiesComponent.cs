using Microsoft.Extensions.Configuration;

namespace Caravan.Spi;

/// <summary>
/// Resolves property placeholders (Camel's <c>PropertiesComponent</c>), available as <see cref="ICaravanContext.PropertiesComponent"/>.
/// <para>
/// Syntax: <c>{{key}}</c>, <c>{{key:default}}</c> (the default may be empty or contain placeholders), nested placeholders in keys
/// (<c>{{app.{{env}}.url}}</c>) and values (a value may contain <c>{{other}}</c>), <c>{{env:NAME}}</c> / <c>{{env:NAME:default}}</c>
/// (environment variables), <c>{{sys:name}}</c> (<see cref="AppContext.GetData(string)"/>, .NET's counterpart of Java system
/// properties) and optional placeholders <c>{{?key}}</c>: when the key is missing, <see cref="ParseUri"/> drops the URI option that
/// uses it (<c>log:a?level={{?lvl}}</c> becomes <c>log:a</c>) and other text gets an empty string.
/// </para>
/// <para>
/// Precedence (first match wins): <see cref="OverrideProperties"/>, environment variables when <see cref="EnvironmentVariableMode"/> is
/// <see cref="Spi.EnvironmentVariableMode.Override"/>, <see cref="Sources"/> in order (<see cref="AddConfiguration"/> adds an
/// <see cref="IConfiguration"/> there), <see cref="InitialProperties"/>, environment variables in
/// <see cref="Spi.EnvironmentVariableMode.Fallback"/> mode, then the placeholder's default value.
/// </para>
/// </summary>
public interface IPropertiesComponent
{
    /// <summary>Properties with the lowest precedence, used when no source defines a key (Camel's <c>initialProperties</c>).</summary>
    IDictionary<string, string?> InitialProperties { get; }

    /// <summary>Properties that take precedence over every source (Camel's <c>overrideProperties</c>).</summary>
    IDictionary<string, string?> OverrideProperties { get; }

    /// <summary>The property sources, tried in order (the same list as <see cref="ICaravanContext.PropertiesSources"/>).</summary>
    IList<IPropertiesSource> Sources { get; }

    /// <summary>Whether and how keys are looked up as environment variables without the <c>env:</c> prefix (default <see cref="Spi.EnvironmentVariableMode.Never"/>).</summary>
    EnvironmentVariableMode EnvironmentVariableMode { get; set; }

    /// <summary>
    /// Adds <paramref name="configuration"/> as a source (after the existing ones). Keys map between Camel's dotted form and
    /// configuration paths: <c>{{app.queue}}</c> reads <c>app.queue</c>, then <c>app:queue</c>.
    /// </summary>
    void AddConfiguration(IConfiguration configuration);

    /// <summary>
    /// The value of <paramref name="key"/> (an <c>env:</c> or <c>sys:</c> prefix is allowed) with the placeholders in it resolved, or
    /// null when no source defines it.
    /// </summary>
    /// <exception cref="ArgumentException">The value refers to a missing property or to itself.</exception>
    string? ResolveProperty(string key);

    /// <summary>Resolves the placeholder <c>{{<paramref name="key"/>}}</c>; the key may carry a default (<c>key:default</c>).</summary>
    /// <exception cref="ArgumentException">The property is missing and has no default.</exception>
    string Resolve(string key);

    /// <summary>Resolves every placeholder in <paramref name="text"/>; missing optional placeholders become empty strings.</summary>
    /// <exception cref="ArgumentException">A placeholder refers to a missing property without default, or properties refer to each other in a cycle.</exception>
    string ResolvePlaceholders(string text);

    /// <summary>
    /// Resolves every placeholder in the endpoint URI <paramref name="uri"/>; a query option whose value uses a missing optional
    /// placeholder (<c>{{?key}}</c>) is removed.
    /// </summary>
    /// <exception cref="ArgumentException">A placeholder refers to a missing property without default, or properties refer to each other in a cycle.</exception>
    string ParseUri(string uri);
}
