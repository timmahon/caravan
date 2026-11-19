using Caravan.Builder;
using Caravan.Model;
using Caravan.Spi;
using Caravan.TypeConversion;
using Microsoft.Extensions.Logging;

namespace Caravan;

/// <summary>
/// The runtime: components, endpoints, routes, registry and type converters (Camel's <c>CamelContext</c>).
/// </summary>
public interface ICaravanContext : IService, IAsyncDisposable
{
    string Name { get; set; }

    IRegistry Registry { get; }

    TypeConverterRegistry TypeConverter { get; }

    TimeProvider TimeProvider { get; set; }

    ILoggerFactory LoggerFactory { get; set; }

    /// <summary>Whether a copy of the original message is kept for <c>useOriginalMessage</c> (default false, as in Camel).</summary>
    bool AllowUseOriginalMessage { get; set; }

    /// <summary>Cancelled when the context is shutting down.</summary>
    CancellationToken ShutdownToken { get; }

    /// <summary>Strategies that wrap every processor node in every route (tracing, metrics, ...).</summary>
    IList<IInterceptStrategy> InterceptStrategies { get; }

    /// <summary>Notified about lifecycle and exchange events.</summary>
    IList<IEventNotifier> EventNotifiers { get; }

    /// <summary>Sources used to resolve <c>{{key}}</c> property placeholders, tried in order.</summary>
    IList<IPropertiesSource> PropertiesSources { get; }

    // Components

    void AddComponent(string scheme, IComponent component);

    /// <summary>Registers a factory used to create the component on first use of its scheme.</summary>
    void AddComponentFactory(string scheme, Func<IComponent> factory);

    IComponent? GetComponent(string scheme);

    T GetComponent<T>(string scheme)
        where T : class, IComponent;

    IReadOnlyCollection<string> ComponentNames { get; }

    // Endpoints

    IEndpoint GetEndpoint(string uri);

    T GetEndpoint<T>(string uri)
        where T : class, IEndpoint;

    /// <summary>Registers an endpoint instance under a URI.</summary>
    void AddEndpoint(string uri, IEndpoint endpoint);

    IReadOnlyCollection<IEndpoint> Endpoints { get; }

    // Routes

    void AddRoutes(IRoutesBuilder builder);

    Task AddRoutesAsync(IRoutesBuilder builder, CancellationToken cancellationToken = default);

    IReadOnlyList<RouteDefinition> RouteDefinitions { get; }

    IReadOnlyList<IRoute> Routes { get; }

    IRoute? GetRoute(string routeId);

    Task StartRouteAsync(string routeId, CancellationToken cancellationToken = default);

    Task StopRouteAsync(string routeId, CancellationToken cancellationToken = default);

    Task<bool> RemoveRouteAsync(string routeId, CancellationToken cancellationToken = default);

    // Misc

    IProducerTemplate CreateProducerTemplate();

    /// <summary>Resolves <c>{{key}}</c> and <c>{{key:default}}</c> placeholders in <paramref name="text"/>.</summary>
    string ResolvePropertyPlaceholders(string text);

    /// <summary>Generates a unique node id such as <c>to3</c>.</summary>
    string NextNodeId(string prefix);

    /// <summary>Adds a service whose lifecycle follows the context.</summary>
    Task AddServiceAsync(object service, CancellationToken cancellationToken = default);

    /// <summary>Extension points keyed by type (used by optional features such as error handling).</summary>
    T? GetExtension<T>()
        where T : class;

    void SetExtension<T>(T extension)
        where T : class;

    /// <summary>Gets an extension, creating it on first use.</summary>
    T GetOrAddExtension<T>(Func<T> factory)
        where T : class;
}
