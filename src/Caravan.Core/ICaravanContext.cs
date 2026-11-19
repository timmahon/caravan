using Caravan.Builder;
using Caravan.Management;
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

    /// <summary>
    /// Generates the ids of exchanges, messages and sagas and the Simple <c>${uuid}</c> function (default
    /// <see cref="Impl.DefaultUuidGenerator"/>). Set it before exchanges are created.
    /// </summary>
    IUuidGenerator UuidGenerator { get; set; }

    /// <summary>
    /// Whether routes start when the context starts (default true; Camel's context <c>autoStartup</c>). When false no route starts with the
    /// context, whatever its own <see cref="RouteDefinition.AutoStartup"/>; start them with <see cref="StartRouteAsync"/>. Routes added
    /// to a running context follow their own option.
    /// </summary>
    bool AutoStartup { get; set; }

    /// <summary>
    /// Comma-separated patterns (exact, wildcard or regular expression, see <see cref="Support.PatternHelper"/>) of the routes to add,
    /// matched against the route id and the <c>from</c> endpoint URI (Camel's <c>routeFilterIncludePattern</c>); other routes are ignored when
    /// added. Null (default) includes every route. Useful to run a subset of the routes in a test.
    /// </summary>
    string? RouteFilterIncludePattern { get; set; }

    /// <summary>
    /// Comma-separated patterns of the routes not to add (Camel's <c>routeFilterExcludePattern</c>); takes precedence over
    /// <see cref="RouteFilterIncludePattern"/>.
    /// </summary>
    string? RouteFilterExcludePattern { get; set; }

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

    // Observability and management

    /// <summary>OpenTelemetry-style tracing and metrics options (see <see cref="Observability.CaravanDiagnostics"/>).</summary>
    Observability.TelemetryOptions Telemetry { get; }

    /// <summary>
    /// Whether every step of every exchange is logged by the <see cref="Tracer"/> (Camel's <c>tracing</c>, default false); shortcut for
    /// <see cref="Observability.CaravanTracer.Enabled"/>. Turn it on before routes are created (or use the tracer's standby mode).
    /// </summary>
    bool Tracing { get; set; }

    /// <summary>The tracer that logs exchange steps (Camel's <c>Tracer</c>).</summary>
    Observability.CaravanTracer Tracer { get; }

    /// <summary>The backlog tracer that keeps the last traced steps in memory (Camel's <c>BacklogTracer</c>).</summary>
    Observability.BacklogTracer BacklogTracer { get; }

    /// <summary>
    /// Whether exchanges record their message history (route inputs and nodes with timings, Camel's <c>messageHistory</c>, default false) in
    /// the <see cref="ExchangePropertyKeys.MessageHistory"/> property; error handlers then log it as a table when redelivery is exhausted. Set it
    /// before routes are created.
    /// </summary>
    bool MessageHistory { get; set; }

    /// <summary>The management API: statistics and operations (Camel's JMX MBeans).</summary>
    IManagementService Management { get; }

    /// <summary>The health checks of the context and its routes, consumers, circuit breakers and components (Camel's health-check registry).</summary>
    Health.HealthCheckRegistry HealthChecks { get; }

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

    /// <summary>The route templates added to the context (they are not routes: instantiate them with <see cref="AddRouteFromTemplateAsync(string?, string, IDictionary{string, object?}, CancellationToken)"/>).</summary>
    IReadOnlyList<RouteTemplateDefinition> RouteTemplateDefinitions { get; }

    /// <summary>Adds (or replaces, by id) a route template.</summary>
    /// <param name="template">The template.</param>
    void AddRouteTemplateDefinition(RouteTemplateDefinition template);

    /// <summary>Removes a route template; routes created from it are kept.</summary>
    /// <param name="templateId">The template id.</param>
    /// <returns>Whether the template existed.</returns>
    bool RemoveRouteTemplateDefinition(string templateId);

    /// <summary>
    /// Creates a route from the template <paramref name="templateId"/> with the given parameters (Camel's <c>addRouteFromTemplate</c>); the
    /// route is created and started right away when the context is running, else when it starts.
    /// </summary>
    /// <param name="routeId">The route id; null uses the id the template's route sets, else a generated one.</param>
    /// <param name="templateId">The template id.</param>
    /// <param name="parameters">The template parameters; they take precedence over the context's properties in <c>{{placeholders}}</c>.</param>
    /// <param name="cancellationToken">Cancels the start of the route.</param>
    /// <returns>The id of the route.</returns>
    /// <exception cref="FailedToCreateRouteFromTemplateException">The template does not exist, a required parameter is missing, a bean
    /// cannot be created or the route cannot be added.</exception>
    Task<string> AddRouteFromTemplateAsync(string? routeId, string templateId, IDictionary<string, object?> parameters, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a route from the template <paramref name="templateId"/> with the parameters and local beans of <paramref name="templateContext"/>.
    /// </summary>
    /// <param name="routeId">The route id; null uses the id the template's route sets, else a generated one.</param>
    /// <param name="templateId">The template id.</param>
    /// <param name="templateContext">The parameters and local beans.</param>
    /// <param name="cancellationToken">Cancels the start of the route.</param>
    /// <returns>The id of the route.</returns>
    /// <exception cref="FailedToCreateRouteFromTemplateException">The route cannot be created.</exception>
    Task<string> AddRouteFromTemplateAsync(string? routeId, string templateId, RouteTemplateContext templateContext, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sources of templated routes created when the context starts (Camel's <c>RouteTemplateParameterSource</c>); <see cref="IRouteTemplateParameterSource"/>
    /// beans in the registry are used too.
    /// </summary>
    IList<IRouteTemplateParameterSource> RouteTemplateParameterSources { get; }

    /// <summary>The route configurations added to the context (see <see cref="RouteConfigurationDefinition"/>).</summary>
    IReadOnlyList<RouteConfigurationDefinition> RouteConfigurationDefinitions { get; }

    /// <summary>
    /// Adds a route configuration. It applies to the routes created afterwards: those that select it by id
    /// (<see cref="RouteDefinition.RouteConfigurationIds"/>) or, when it has no id, those that select none.
    /// </summary>
    /// <param name="configuration">The route configuration.</param>
    void AddRouteConfiguration(RouteConfigurationDefinition configuration);

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
