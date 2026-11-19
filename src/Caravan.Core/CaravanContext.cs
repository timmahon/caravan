using System.Collections.Concurrent;
using Caravan.Builder;
using Caravan.Components.Bean;
using Caravan.Components.Browse;
using Caravan.Components.ControlBus;
using Caravan.Components.DataFormat;
using Caravan.Components.DataSet;
using Caravan.Components.Direct;
using Caravan.Components.Log;
using Caravan.Components.Mock;
using Caravan.Components.Ref;
using Caravan.Components.Saga;
using Caravan.Components.Scheduler;
using Caravan.Components.Seda;
using Caravan.Components.Stub;
using Caravan.Components.Timer;
using Caravan.DataFormat;
using Caravan.Impl;
using Caravan.Language;
using Caravan.Model;
using Caravan.Processors;
using Caravan.Spi;
using Caravan.Streams;
using Caravan.Support;
using Caravan.TypeConversion;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Caravan;

/// <summary>
/// The default <see cref="ICaravanContext"/>:
/// <code>
/// await using var context = new CaravanContext();
/// context.AddRoutes(RouteBuilder.Create(b => b.From("direct:in").To("log:out")));
/// await context.StartAsync();
/// await context.CreateProducerTemplate().SendBodyAsync("direct:in", "hello");
/// </code>
/// </summary>
public class CaravanContext : ServiceSupport, ICaravanContext
{
    private static readonly ConcurrentDictionary<string, Func<IComponent>> GlobalComponentFactories =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly ConcurrentDictionary<string, IComponent> _components = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Func<IComponent>> _componentFactories = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, IEndpoint> _endpoints = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, int> _nodeIdCounters = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<Type, object> _extensions = new();
    private readonly List<RouteDefinition> _routeDefinitions = [];
    private readonly List<Route> _routes = [];
    private readonly List<object> _services = [];
    private readonly object _routesLock = new();
    private CancellationTokenSource _shutdown = new();
    private int _routeCounter;

    static CaravanContext()
    {
        RegisterGlobalComponent("direct", () => new DirectComponent());
        RegisterGlobalComponent("log", () => new LogComponent());
        RegisterGlobalComponent("mock", () => new MockComponent());
        RegisterGlobalComponent("seda", () => new SedaComponent());
        RegisterGlobalComponent("vm", () => new VmComponent());
        RegisterGlobalComponent("timer", () => new TimerComponent());
        RegisterGlobalComponent("scheduler", () => new SchedulerComponent());
        RegisterGlobalComponent("saga", () => new SagaComponent());
        RegisterGlobalComponent("dataformat", () => new DataFormatComponent());
        RegisterGlobalComponent("bean", () => new BeanComponent());
        RegisterGlobalComponent("class", () => new ClassComponent());
        RegisterGlobalComponent("controlbus", () => new ControlBusComponent());
        RegisterGlobalComponent("stub", () => new StubComponent());
        RegisterGlobalComponent("ref", () => new RefComponent());
        RegisterGlobalComponent("dataset", () => new DataSetComponent());
        RegisterGlobalComponent("dataset-test", () => new DataSetTestComponent());
        RegisterGlobalComponent("browse", () => new BrowseComponent());
    }

    /// <summary>Creates a context.</summary>
    /// <param name="services">Optional service provider used as a fallback by the registry for bean lookups.</param>
    /// <remarks>When <paramref name="services"/> provides an <see cref="IConfiguration"/>, it is added as a properties source.</remarks>
    public CaravanContext(IServiceProvider? services = null)
    {
        Registry = new DefaultRegistry(services);
        Services = services;
        PropertiesComponent = new DefaultPropertiesComponent(PropertiesSources);
        Languages = new LanguageRegistry(this);
        DataFormats = new DataFormatRegistry(this);
        if (services?.GetService(typeof(IConfiguration)) is IConfiguration configuration)
        {
            PropertiesComponent.AddConfiguration(configuration);
        }
    }

    /// <summary>
    /// Registers a component factory available to every context (used by component packages,
    /// equivalent to Camel's classpath component discovery).
    /// </summary>
    public static void RegisterGlobalComponent(string scheme, Func<IComponent> factory) =>
        GlobalComponentFactories[scheme] = factory;

    /// <inheritdoc />
    public string Name { get; set; } = "caravan-1";

    /// <summary>The service provider backing the registry, if any.</summary>
    public IServiceProvider? Services { get; }

    /// <inheritdoc />
    public IRegistry Registry { get; }

    /// <inheritdoc />
    public TypeConverterRegistry TypeConverter { get; } = new();

    /// <inheritdoc />
    public TimeProvider TimeProvider { get; set; } = TimeProvider.System;

    /// <inheritdoc />
    public ILoggerFactory LoggerFactory { get; set; } = NullLoggerFactory.Instance;

    /// <inheritdoc />
    public bool AllowUseOriginalMessage { get; set; }

    /// <inheritdoc />
    public ErrorHandlerDefinition? ErrorHandler { get; set; }

    /// <inheritdoc />
    public CancellationToken ShutdownToken => _shutdown.Token;

    /// <inheritdoc />
    public IList<IInterceptStrategy> InterceptStrategies { get; } = new List<IInterceptStrategy>();

    /// <inheritdoc />
    /// <remarks>Thread-safe: notifiers may be added or removed while exchanges are routed.</remarks>
    public IList<IEventNotifier> EventNotifiers { get; } = new CopyOnWriteList<IEventNotifier>();

    /// <inheritdoc />
    public IList<IPropertiesSource> PropertiesSources { get; } = new CopyOnWriteList<IPropertiesSource>();

    /// <inheritdoc />
    public IPropertiesComponent PropertiesComponent { get; }

    /// <inheritdoc />
    public LanguageRegistry Languages { get; }

    /// <inheritdoc />
    public ILanguage ResolveLanguage(string name) => Languages.Resolve(name);

    /// <inheritdoc />
    public DataFormatRegistry DataFormats { get; }

    /// <inheritdoc />
    public IDataFormat ResolveDataFormat(string name) => DataFormats.Resolve(name);

    /// <inheritdoc />
    public StreamCachingStrategy StreamCaching { get; } = new();

    /// <inheritdoc cref="ICaravanContext.InflightRepository" />
    /// <remarks>Can be replaced before the context starts.</remarks>
    public IInflightRepository InflightRepository { get; set; } = new DefaultInflightRepository();

    /// <inheritdoc />
    public IShutdownStrategy ShutdownStrategy { get; set; } = new DefaultShutdownStrategy();

    /// <inheritdoc />
    public SupervisingRouteController? SupervisingRouteController { get; set; }

    // ----- Components -----

    /// <inheritdoc />
    public void AddComponent(string scheme, IComponent component)
    {
        component.Context = this;
        if (!_components.TryAdd(scheme, component))
        {
            throw new InvalidOperationException($"Component already registered for scheme: {scheme}");
        }

        if (IsStarted)
        {
            component.StartAsync().GetAwaiter().GetResult();
        }
    }

    /// <inheritdoc />
    public void AddComponentFactory(string scheme, Func<IComponent> factory) => _componentFactories[scheme] = factory;

    /// <inheritdoc />
    public IComponent? GetComponent(string scheme)
    {
        if (_components.TryGetValue(scheme, out var existing))
        {
            return existing;
        }

        var factory = _componentFactories.GetValueOrDefault(scheme) ?? GlobalComponentFactories.GetValueOrDefault(scheme);
        var fromRegistry = factory is null ? Registry.Lookup(scheme) as IComponent : null;
        if (factory is null && fromRegistry is null)
        {
            return null;
        }

        var created = _components.GetOrAdd(scheme, _ =>
        {
            var component = fromRegistry ?? factory!();
            component.Context = this;
            return component;
        });

        if (IsRunAllowed && created.Status != ServiceStatus.Started)
        {
            created.StartAsync().GetAwaiter().GetResult();
        }

        return created;
    }

    /// <inheritdoc />
    public T GetComponent<T>(string scheme)
        where T : class, IComponent =>
        GetComponent(scheme) as T ?? throw new InvalidOperationException($"No component of type {typeof(T).Name} for scheme {scheme}");

    /// <inheritdoc />
    public IReadOnlyCollection<string> ComponentNames =>
        _components.Keys.Union(_componentFactories.Keys).Union(GlobalComponentFactories.Keys, StringComparer.OrdinalIgnoreCase).ToList();

    // ----- Endpoints -----

    /// <inheritdoc />
    public IEndpoint GetEndpoint(string uri)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(uri);
        var resolved = ResolvePropertyPlaceholders(uri);
        var key = EndpointUri.Normalize(resolved);
        if (_endpoints.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var scheme = EndpointUri.Parse(resolved).Scheme;
        var component = GetComponent(scheme) ?? throw new NoSuchEndpointException(resolved, $"No component found with scheme: {scheme}");
        var endpoint = component.CreateEndpoint(resolved);
        if (!endpoint.IsSingleton)
        {
            StartIfRunning(endpoint);
            return endpoint;
        }

        var added = _endpoints.GetOrAdd(key, endpoint);
        if (ReferenceEquals(added, endpoint))
        {
            StartIfRunning(endpoint);
        }

        return added;
    }

    /// <inheritdoc />
    public T GetEndpoint<T>(string uri)
        where T : class, IEndpoint =>
        GetEndpoint(uri) as T ?? throw new InvalidOperationException($"Endpoint {uri} is not of type {typeof(T).Name}");

    /// <inheritdoc />
    public void AddEndpoint(string uri, IEndpoint endpoint)
    {
        _endpoints[EndpointUri.Normalize(ResolvePropertyPlaceholders(uri))] = endpoint;
        StartIfRunning(endpoint);
    }

    /// <inheritdoc />
    public IReadOnlyCollection<IEndpoint> Endpoints => _endpoints.Values.Distinct().ToList();

    private void StartIfRunning(IService service)
    {
        if (IsRunAllowed)
        {
            service.StartAsync().GetAwaiter().GetResult();
        }
    }

    // ----- Routes -----

    /// <inheritdoc />
    public IReadOnlyList<RouteDefinition> RouteDefinitions
    {
        get
        {
            lock (_routesLock)
            {
                return [.. _routeDefinitions];
            }
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<IRoute> Routes
    {
        get
        {
            lock (_routesLock)
            {
                return [.. _routes];
            }
        }
    }

    /// <inheritdoc />
    public IRoute? GetRoute(string routeId)
    {
        lock (_routesLock)
        {
            return _routes.FirstOrDefault(r => r.Id == routeId);
        }
    }

    /// <inheritdoc />
    public void AddRoutes(IRoutesBuilder builder)
    {
        if (IsRunAllowed)
        {
            AddRoutesAsync(builder).GetAwaiter().GetResult();
            return;
        }

        var definitions = builder.Build(this);
        lock (_routesLock)
        {
            foreach (var definition in definitions)
            {
                ResolvePlaceholders(definition);
                AssignRouteId(definition);
                _routeDefinitions.Add(definition);
            }
        }
    }

    /// <inheritdoc />
    public async Task AddRoutesAsync(IRoutesBuilder builder, CancellationToken cancellationToken = default)
    {
        var definitions = builder.Build(this);
        var created = new List<Route>();
        lock (_routesLock)
        {
            foreach (var definition in definitions)
            {
                ResolvePlaceholders(definition);
                AssignRouteId(definition);
                _routeDefinitions.Add(definition);
            }
        }

        if (!IsRunAllowed)
        {
            return;
        }

        foreach (var definition in definitions)
        {
            created.Add(CreateRoute(definition));
        }

        foreach (var route in OrderForStartup(created))
        {
            if (route.Definition.AutoStartup)
            {
                await StartRouteInternalAsync(route, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <inheritdoc />
    public async Task StartRouteAsync(string routeId, CancellationToken cancellationToken = default)
    {
        var route = (Route?)GetRoute(routeId) ?? throw new ArgumentException($"No route with id: {routeId}", nameof(routeId));
        await StartRouteInternalAsync(route, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task StopRouteAsync(string routeId, CancellationToken cancellationToken = default)
    {
        var route = (Route?)GetRoute(routeId) ?? throw new ArgumentException($"No route with id: {routeId}", nameof(routeId));
        await StopRouteInternalAsync(route, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<bool> RemoveRouteAsync(string routeId, CancellationToken cancellationToken = default)
    {
        var route = (Route?)GetRoute(routeId);
        if (route is null)
        {
            lock (_routesLock)
            {
                return _routeDefinitions.RemoveAll(d => d.Id == routeId) > 0;
            }
        }

        if (route.Status is not (ServiceStatus.Stopped or ServiceStatus.Initialized or ServiceStatus.Failed))
        {
            return false;
        }

        SupervisingRouteController?.Forget(routeId);

        lock (_routesLock)
        {
            _routes.Remove(route);
            _routeDefinitions.Remove(route.Definition);
        }

        EventHelper.Notify(this, CaravanEventType.RouteRemoved, routeId: routeId);
        return true;
    }

    /// <inheritdoc />
    public async Task SuspendRouteAsync(string routeId, CancellationToken cancellationToken = default)
    {
        var route = RequireRoute(routeId);
        if (route.Status != ServiceStatus.Started)
        {
            return;
        }

        await ShutdownStrategy.SuspendAsync(this, [route], cancellationToken: cancellationToken).ConfigureAwait(false);
        await route.SuspendAsync(cancellationToken).ConfigureAwait(false);
        EventHelper.Notify(this, CaravanEventType.RouteSuspended, routeId: route.Id, endpoint: route.Endpoint);
    }

    /// <inheritdoc />
    public async Task ResumeRouteAsync(string routeId, CancellationToken cancellationToken = default)
    {
        var route = RequireRoute(routeId);
        if (route.Status == ServiceStatus.Suspended)
        {
            await route.ResumeAsync(cancellationToken).ConfigureAwait(false);
            EventHelper.Notify(this, CaravanEventType.RouteResumed, routeId: route.Id, endpoint: route.Endpoint);
        }
        else if (route.Status != ServiceStatus.Started)
        {
            await StartRouteInternalAsync(route, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public ServiceStatus? GetRouteStatus(string routeId) => GetRoute(routeId)?.Status;

    private Route RequireRoute(string routeId) =>
        (Route?)GetRoute(routeId) ?? throw new ArgumentException($"No route with id: {routeId}", nameof(routeId));

    internal async Task StartRouteInternalAsync(Route route, CancellationToken cancellationToken)
    {
        try
        {
            await route.StartAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not CaravanException and not OperationCanceledException)
        {
            throw new FailedToStartRouteException(route.Id, e.Message, e);
        }

        EventHelper.Notify(this, CaravanEventType.RouteStarted, routeId: route.Id, endpoint: route.Endpoint);
    }

    private async Task StopRouteInternalAsync(Route route, CancellationToken cancellationToken)
    {
        SupervisingRouteController?.Forget(route.Id);
        if (route.Status is ServiceStatus.Started or ServiceStatus.Suspended)
        {
            await ShutdownStrategy.ShutdownAsync(this, [route], cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        await route.StopAsync(cancellationToken).ConfigureAwait(false);
        EventHelper.Notify(this, CaravanEventType.RouteStopped, routeId: route.Id, endpoint: route.Endpoint);
    }

    /// <summary>Cancels <see cref="ShutdownToken"/> (used by the shutdown strategy to force a context shutdown).</summary>
    internal Task CancelShutdownTokenAsync() => _shutdown.CancelAsync();

    /// <summary>Resolves property placeholders in the ids, group and descriptions of a route and its nodes when it is added (endpoint URIs are resolved when endpoints are created).</summary>
    private void ResolvePlaceholders(RouteDefinition definition)
    {
        definition.Id = Resolve(definition.Id);
        definition.Group = Resolve(definition.Group);
        definition.Description = Resolve(definition.Description);
        definition.FromId = Resolve(definition.FromId);
        foreach (var node in definition.AllNodes())
        {
            node.Id = Resolve(node.Id);
            node.Description = Resolve(node.Description);
        }

        string? Resolve(string? text) => text is null ? null : ResolvePropertyPlaceholders(text);
    }

    private void AssignRouteId(RouteDefinition definition)
    {
        if (definition.Id is null)
        {
            string id;
            do
            {
                id = "route" + Interlocked.Increment(ref _routeCounter);
            }
            while (_routeDefinitions.Any(d => d.Id == id));

            definition.Id = id;
        }
        else if (_routeDefinitions.Any(d => d.Id == definition.Id))
        {
            throw new FailedToCreateRouteException(definition.Id, "Duplicate route id");
        }
    }

    private Route CreateRoute(RouteDefinition definition)
    {
        var routeId = definition.Id!;
        try
        {
            SimpleRouteValidator.Validate(definition);
            var routeContext = new RouteContext(this, definition, routeId);
            var errorHandler = definition.ErrorHandler ?? ErrorHandler ?? new DefaultErrorHandlerDefinition();
            if (errorHandler.RequiresOriginalMessage ||
                definition.OnExceptions.Any(o => o.UseOriginalMessage || o.UseOriginalBody) ||
                definition.OnCompletions.Any(o => o.UseOriginalMessage))
            {
                AllowUseOriginalMessage = true;
            }

            routeContext.ErrorHandlerFactory = errorHandler.CreateFactory(routeContext);
            foreach (var configurer in GetExtension<RouteContextConfigurers>()?.Configurers ?? [])
            {
                configurer(routeContext);
            }

            var endpoint = GetEndpoint(definition.FromUri);
            RegisterInterceptSendToEndpoints(definition);
            var intercepts = definition.Intercepts
                .Where(i => !i.Disabled)
                .Select(i => new InterceptNodeStrategy(i, InterceptFromProcessor.BuildBlock(routeContext, i), endpoint.EndpointUri))
                .ToList();
            foreach (var intercept in intercepts)
            {
                routeContext.InterceptStrategies.Add(intercept);
            }

            var pipeline = routeContext.CreateBlockProcessor(definition.Outputs);
            pipeline = InterceptFromProcessor.Wrap(routeContext, endpoint, pipeline);
            pipeline = OnCompletionProcessor.Wrap(routeContext, pipeline);
            var route = new Route(routeContext, endpoint, pipeline);
            lock (_routesLock)
            {
                _routes.Add(route);
            }

            EventHelper.Notify(this, CaravanEventType.RouteAdded, routeId: routeId, endpoint: endpoint);
            return route;
        }
        catch (Exception e) when (e is not FailedToCreateRouteException)
        {
            throw new FailedToCreateRouteException(routeId, e.Message, e);
        }
    }

    private void RegisterInterceptSendToEndpoints(RouteDefinition definition)
    {
        var interceptors = SendToEndpointInterceptors.For(this);
        foreach (var intercept in definition.InterceptSendToEndpoints.Where(i => !i.Disabled))
        {
            var interceptor = interceptors.GetOrAdd(
                existing => ReferenceEquals(existing.Definition, intercept),
                () => new InterceptSendToEndpointInterceptor(this, intercept));
            AddServiceAsync(interceptor).GetAwaiter().GetResult();
        }
    }

    private static IEnumerable<Route> OrderForStartup(IEnumerable<Route> routes) =>
        routes.Select((r, i) => (r, i))
            .OrderBy(x => x.r.Definition.StartupOrder ?? int.MaxValue)
            .ThenBy(x => x.i)
            .Select(x => x.r);

    // ----- Lifecycle -----

    /// <summary>Starts services, components and endpoints, creates the routes and starts those with auto-startup.</summary>
    protected override async Task DoStartAsync(CancellationToken cancellationToken)
    {
        if (_shutdown.IsCancellationRequested)
        {
            _shutdown.Dispose();
            _shutdown = new CancellationTokenSource();
        }

        EventHelper.Notify(this, CaravanEventType.ContextStarting);

        foreach (var service in _services.ToList())
        {
            await StartServiceAsync(service, cancellationToken).ConfigureAwait(false);
        }

        foreach (var component in _components.Values)
        {
            await component.StartAsync(cancellationToken).ConfigureAwait(false);
        }

        foreach (var endpoint in _endpoints.Values.Distinct())
        {
            await endpoint.StartAsync(cancellationToken).ConfigureAwait(false);
        }

        List<RouteDefinition> definitions;
        lock (_routesLock)
        {
            definitions = _routeDefinitions.Where(d => _routes.All(r => r.Definition != d)).ToList();
        }

        foreach (var definition in definitions)
        {
            CreateRoute(definition);
        }

        var toStart = OrderForStartup(Routes.Cast<Route>())
            .Where(r => r.Definition.AutoStartup && r.Status != ServiceStatus.Started)
            .ToList();
        if (SupervisingRouteController is { } supervisor)
        {
            await supervisor.StartRoutesAsync(this, toStart, ShutdownToken).ConfigureAwait(false);
        }
        else
        {
            foreach (var route in toStart)
            {
                await StartRouteInternalAsync(route, cancellationToken).ConfigureAwait(false);
            }
        }

        EventHelper.Notify(this, CaravanEventType.ContextStarted);
    }

    /// <summary>
    /// Shuts the routes down gracefully with the <see cref="ShutdownStrategy"/> (reverse startup order, deferred routes last),
    /// stops them, then stops endpoints, components and services.
    /// </summary>
    protected override async Task DoStopAsync(CancellationToken cancellationToken)
    {
        EventHelper.Notify(this, CaravanEventType.ContextStopping);

        if (SupervisingRouteController is { } supervisor)
        {
            await supervisor.StopAsync().ConfigureAwait(false);
        }

        var reversed = OrderForStartup(Routes.Cast<Route>()).Reverse().ToList();
        var ordered = reversed.Where(r => r.Definition.ShutdownRoute != ShutdownRoute.Defer)
            .Concat(reversed.Where(r => r.Definition.ShutdownRoute == ShutdownRoute.Defer))
            .ToList();
        var running = ordered.Where(r => r.Status is ServiceStatus.Started or ServiceStatus.Suspended).Cast<IRoute>().ToList();
        await ShutdownStrategy.ShutdownAsync(this, running, cancellationToken: cancellationToken).ConfigureAwait(false);

        foreach (var route in ordered)
        {
            await route.StopAsync(cancellationToken).ConfigureAwait(false);
            EventHelper.Notify(this, CaravanEventType.RouteStopped, routeId: route.Id, endpoint: route.Endpoint);
        }

        await _shutdown.CancelAsync().ConfigureAwait(false);

        foreach (var endpoint in _endpoints.Values.Distinct())
        {
            await endpoint.StopAsync(cancellationToken).ConfigureAwait(false);
        }

        foreach (var component in _components.Values)
        {
            await component.StopAsync(cancellationToken).ConfigureAwait(false);
        }

        foreach (var service in Enumerable.Reverse(_services.ToList()))
        {
            await StopServiceAsync(service, cancellationToken).ConfigureAwait(false);
        }

        lock (_routesLock)
        {
            _routes.Clear();
        }

        StreamCaching.Stop();
        EventHelper.Notify(this, CaravanEventType.ContextStopped);
    }

    /// <summary>Stops the context.</summary>
    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _shutdown.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <inheritdoc />
    public async Task AddServiceAsync(object service, CancellationToken cancellationToken = default)
    {
        lock (_services)
        {
            if (_services.Contains(service))
            {
                return;
            }

            _services.Add(service);
        }

        if (IsRunAllowed)
        {
            await StartServiceAsync(service, cancellationToken).ConfigureAwait(false);
        }
    }

    // ----- Misc -----

    /// <inheritdoc />
    public IProducerTemplate CreateProducerTemplate() => new ProducerTemplate(this);

    /// <inheritdoc />
    public IConsumerTemplate CreateConsumerTemplate() => new ConsumerTemplate(this);

    /// <inheritdoc />
    public string NextNodeId(string prefix) =>
        prefix + _nodeIdCounters.AddOrUpdate(prefix, 1, (_, n) => n + 1);

    /// <inheritdoc />
    public string ResolvePropertyPlaceholders(string text) => PropertiesComponent.ParseUri(text);

    /// <inheritdoc />
    public T? GetExtension<T>()
        where T : class => _extensions.TryGetValue(typeof(T), out var value) ? (T)value : null;

    /// <inheritdoc />
    public void SetExtension<T>(T extension)
        where T : class => _extensions[typeof(T)] = extension;

    /// <inheritdoc />
    public T GetOrAddExtension<T>(Func<T> factory)
        where T : class => (T)_extensions.GetOrAdd(typeof(T), _ => factory());

    internal ILogger CreateLogger(string category) => LoggerFactory.CreateLogger(category);
}
