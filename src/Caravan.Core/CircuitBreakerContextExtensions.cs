using Caravan.Impl;
using Caravan.Model;
using Caravan.Spi;

namespace Caravan;

/// <summary>
/// Context-level settings of the <c>circuitBreaker</c> EIP: the default and named configurations (Camel's <c>resilience4jConfiguration</c>)
/// and the registry of circuit states.
/// </summary>
public static class CircuitBreakerContextExtensions
{
    /// <summary>
    /// Sets the configuration every <c>circuitBreaker</c> of the context starts from (a referenced configuration and the EIP's own options
    /// override it). Set it before the routes are created.
    /// </summary>
    /// <param name="context">The context.</param>
    /// <param name="configuration">The default configuration.</param>
    public static void SetDefaultCircuitBreakerConfiguration(this ICaravanContext context, CircuitBreakerConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        Configurations(context).Default = configuration;
    }

    /// <summary>
    /// Adds a named configuration that <c>circuitBreaker</c> EIPs reference with <c>ConfigurationRef(name)</c>. A
    /// <see cref="CircuitBreakerConfiguration"/> bound in the registry under the name works too; the context's one wins.
    /// </summary>
    /// <param name="context">The context.</param>
    /// <param name="name">The name.</param>
    /// <param name="configuration">The configuration.</param>
    public static void AddCircuitBreakerConfiguration(this ICaravanContext context, string name, CircuitBreakerConfiguration configuration)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(configuration);
        Configurations(context).Named[name] = configuration;
    }

    /// <summary>The circuits of the context's <c>circuitBreaker</c> EIPs, by node id (for management).</summary>
    /// <param name="context">The context.</param>
    /// <returns>The registry.</returns>
    public static ICircuitBreakerStateRegistry GetCircuitBreakerStateRegistry(this ICaravanContext context) =>
        context.GetOrAddExtension(() => new DefaultCircuitBreakerStateRegistry());

    internal static CircuitBreakerConfigurations Configurations(ICaravanContext context) =>
        context.GetOrAddExtension(() => new CircuitBreakerConfigurations());
}
