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

    /// <summary>
    /// The default error handler of the context's routes, used by routes (and route builders) that configure none; when null a
    /// <see cref="Model.DefaultErrorHandlerDefinition"/> is used. Takes effect for routes created afterwards.
    /// </summary>
    Model.ErrorHandlerDefinition? ErrorHandler { get; set; }

    /// <summary>Cancelled when the context is shutting down.</summary>
    CancellationToken ShutdownToken { get; }

    /// <summary>Strategies that wrap every processor node in every route (tracing, metrics, ...).</summary>
    IList<IInterceptStrategy> InterceptStrategies { get; }

    /// <summary>Notified about lifecycle and exchange events.</summary>
    IList<IEventNotifier> EventNotifiers { get; }

    /// <summary>Sources used to resolve <c>{{key}}</c> property placeholders, tried in order (<see cref="IPropertiesComponent.Sources"/>).</summary>
    IList<IPropertiesSource> PropertiesSources { get; }

    /// <summary>Resolves <c>{{key}}</c> property placeholders (Camel's <c>PropertiesComponent</c>).</summary>
    IPropertiesComponent PropertiesComponent { get; }

    /// <summary>The expression languages of the context (see <see cref="ResolveLanguage"/>).</summary>
    Language.LanguageRegistry Languages { get; }

    /// <summary>
    /// The language named <paramref name="name"/> (<c>simple</c>, <c>constant</c>, <c>header</c>, <c>exchangeProperty</c>,
    /// <c>tokenize</c>, <c>ref</c>, or one added to <see cref="Languages"/> or bound in the registry), e.g.
    /// <c>context.ResolveLanguage("simple").CreatePredicate("${header.x} &gt; 5")</c>.
    /// </summary>
    /// <exception cref="NoSuchLanguageException">There is no such language.</exception>
    ILanguage ResolveLanguage(string name);

    /// <summary>The data formats of the context (see <see cref="ResolveDataFormat"/>).</summary>
    DataFormat.DataFormatRegistry DataFormats { get; }

    /// <summary>
    /// The data format named <paramref name="name"/> (<c>json</c>, <c>xml</c>, <c>csv</c>, <c>base64</c>, <c>gzipDeflater</c>, <c>zipDeflater</c>,
    /// <c>string</c>, or one added to <see cref="DataFormats"/> or bound in the registry), used by <c>marshal</c>/<c>unmarshal</c>.
    /// </summary>
    /// <exception cref="NoSuchDataFormatException">There is no such data format.</exception>
    IDataFormat ResolveDataFormat(string name);

    /// <summary>
    /// Stream caching (Camel's stream caching strategy): whether routes cache stream bodies so they can be read several times, and where
    /// large ones are spooled. Enabled by default; configure it before routes are created.
    /// </summary>
    Streams.StreamCachingStrategy StreamCaching { get; }

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

    /// <summary>Removes a stopped route; returns false when it does not exist or is not stopped.</summary>
    Task<bool> RemoveRouteAsync(string routeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Suspends a started route: its consumer stops taking new messages (consumers that cannot be suspended are stopped)
    /// and inflight exchanges complete, but the route stays registered and can be resumed. Does nothing unless the route is started.
    /// </summary>
    /// <exception cref="ArgumentException">There is no such route.</exception>
    Task SuspendRouteAsync(string routeId, CancellationToken cancellationToken = default);

    /// <summary>Resumes a suspended route (starts it when it is stopped).</summary>
    /// <exception cref="ArgumentException">There is no such route.</exception>
    Task ResumeRouteAsync(string routeId, CancellationToken cancellationToken = default);

    /// <summary>The status of a route, or null when there is no such runtime route (e.g. before the context started).</summary>
    ServiceStatus? GetRouteStatus(string routeId);

    /// <summary>The exchanges currently being routed.</summary>
    IInflightRepository InflightRepository { get; }

    /// <summary>Stops and suspends routes gracefully; set it (or its <see cref="IShutdownStrategy.Timeout"/>) before stopping the context.</summary>
    IShutdownStrategy ShutdownStrategy { get; set; }

    /// <summary>
    /// When set (before the context starts), routes that fail to start are retried in the background instead of failing
    /// the context startup. Null (default) disables supervision.
    /// </summary>
    Impl.SupervisingRouteController? SupervisingRouteController { get; set; }

    // Misc

    /// <summary>Creates a template for sending messages to endpoints from application code.</summary>
    IProducerTemplate CreateProducerTemplate();

    /// <summary>Creates a template for receiving (polling) messages from endpoints in application code.</summary>
    IConsumerTemplate CreateConsumerTemplate();

    /// <summary>
    /// Resolves <c>{{key}}</c>, <c>{{key:default}}</c>, <c>{{env:NAME}}</c> and optional <c>{{?key}}</c> placeholders in
    /// <paramref name="text"/> (an endpoint URI or any other text) with <see cref="PropertiesComponent"/>
    /// (<see cref="IPropertiesComponent.ParseUri"/>).
    /// </summary>
    /// <exception cref="ArgumentException">A placeholder refers to a missing property without default.</exception>
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
