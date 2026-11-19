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
    /// <summary>The context name, used in logs and management.</summary>
    string Name { get; set; }

    /// <summary>Named and typed beans (processors, components, ...), looked up by <c>#name</c> URI references.</summary>
    IRegistry Registry { get; }

    /// <summary>Converts bodies, headers and URI options between types.</summary>
    TypeConverterRegistry TypeConverter { get; }

    /// <summary>The clock used for timestamps, delays and timers; replace it in tests.</summary>
    TimeProvider TimeProvider { get; set; }

    /// <summary>Creates the loggers used by the context, components and EIPs.</summary>
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

    /// <summary>Registers a component instance for <paramref name="scheme"/>.</summary>
    void AddComponent(string scheme, IComponent component);

    /// <summary>Registers a factory used to create the component on first use of its scheme.</summary>
    void AddComponentFactory(string scheme, Func<IComponent> factory);

    /// <summary>Gets the component for <paramref name="scheme"/>, creating it from a registered factory, the registry or a global factory; null when there is none.</summary>
    IComponent? GetComponent(string scheme);

    /// <summary>Gets the component for <paramref name="scheme"/> as <typeparamref name="T"/>.</summary>
    /// <exception cref="InvalidOperationException">There is no such component or it is of another type.</exception>
    T GetComponent<T>(string scheme)
        where T : class, IComponent;

    /// <summary>The schemes of the components created so far.</summary>
    IReadOnlyCollection<string> ComponentNames { get; }

    // Endpoints

    /// <summary>Gets or creates the endpoint for <paramref name="uri"/>, resolving placeholders; endpoints are cached by normalized URI.</summary>
    /// <exception cref="NoSuchEndpointException">No component exists for the URI scheme.</exception>
    IEndpoint GetEndpoint(string uri);

    /// <summary>Gets the endpoint for <paramref name="uri"/> as <typeparamref name="T"/>.</summary>
    T GetEndpoint<T>(string uri)
        where T : class, IEndpoint;

    /// <summary>Registers an endpoint instance under a URI.</summary>
    void AddEndpoint(string uri, IEndpoint endpoint);

    /// <summary>The cached endpoints.</summary>
    IReadOnlyCollection<IEndpoint> Endpoints { get; }

    // Routes

    /// <summary>Adds the routes of <paramref name="builder"/>; they are created when the context starts.</summary>
    void AddRoutes(IRoutesBuilder builder);

    /// <summary>Adds the routes of <paramref name="builder"/>, creating and starting them right away if the context is running.</summary>
    Task AddRoutesAsync(IRoutesBuilder builder, CancellationToken cancellationToken = default);

    /// <summary>The definitions of all routes added to the context.</summary>
    IReadOnlyList<RouteDefinition> RouteDefinitions { get; }

    /// <summary>The runtime routes (created when the context starts).</summary>
    IReadOnlyList<IRoute> Routes { get; }

    /// <summary>Gets a route by id, or null.</summary>
    IRoute? GetRoute(string routeId);

    /// <summary>Starts a route.</summary>
    Task StartRouteAsync(string routeId, CancellationToken cancellationToken = default);

    /// <summary>Stops a route; inflight exchanges are allowed to complete.</summary>
    Task StopRouteAsync(string routeId, CancellationToken cancellationToken = default);

    /// <summary>Stops and removes a route; returns false when it does not exist.</summary>
    Task<bool> RemoveRouteAsync(string routeId, CancellationToken cancellationToken = default);

    // Misc

    /// <summary>Creates a template for sending messages to endpoints from application code.</summary>
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

    /// <summary>Stores (or replaces) the extension of type <typeparamref name="T"/>.</summary>
    void SetExtension<T>(T extension)
        where T : class;

    /// <summary>Gets an extension, creating it on first use.</summary>
    T GetOrAddExtension<T>(Func<T> factory)
        where T : class;
}
