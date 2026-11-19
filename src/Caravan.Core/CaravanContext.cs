using System.Collections.Concurrent;
using System.Text;
using Caravan.Builder;
using Caravan.Components.Direct;
using Caravan.Components.Log;
using Caravan.Components.Mock;
using Caravan.Impl;
using Caravan.Model;
using Caravan.Processors;
using Caravan.Spi;
using Caravan.Support;
using Caravan.TypeConversion;
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
    }

    public CaravanContext(IServiceProvider? services = null)
    {
        Registry = new DefaultRegistry(services);
        Services = services;
    }

    /// <summary>
    /// Registers a component factory available to every context (used by component packages,
    /// equivalent to Camel's classpath component discovery).
    /// </summary>
    public static void RegisterGlobalComponent(string scheme, Func<IComponent> factory) =>
        GlobalComponentFactories[scheme] = factory;

    public string Name { get; set; } = "caravan-1";

    public IServiceProvider? Services { get; }

    public IRegistry Registry { get; }

    public TypeConverterRegistry TypeConverter { get; } = new();

    public TimeProvider TimeProvider { get; set; } = TimeProvider.System;

    public ILoggerFactory LoggerFactory { get; set; } = NullLoggerFactory.Instance;

    public bool AllowUseOriginalMessage { get; set; }

    public CancellationToken ShutdownToken => _shutdown.Token;

    public IList<IInterceptStrategy> InterceptStrategies { get; } = new List<IInterceptStrategy>();

    public IList<IEventNotifier> EventNotifiers { get; } = new List<IEventNotifier>();

    public IList<IPropertiesSource> PropertiesSources { get; } = new List<IPropertiesSource>();

    // ----- Components -----

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

    public void AddComponentFactory(string scheme, Func<IComponent> factory) => _componentFactories[scheme] = factory;

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

    public T GetComponent<T>(string scheme)
        where T : class, IComponent =>
        GetComponent(scheme) as T ?? throw new InvalidOperationException($"No component of type {typeof(T).Name} for scheme {scheme}");

    public IReadOnlyCollection<string> ComponentNames =>
        _components.Keys.Union(_componentFactories.Keys).Union(GlobalComponentFactories.Keys, StringComparer.OrdinalIgnoreCase).ToList();

    // ----- Endpoints -----

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

    public T GetEndpoint<T>(string uri)
        where T : class, IEndpoint =>
        GetEndpoint(uri) as T ?? throw new InvalidOperationException($"Endpoint {uri} is not of type {typeof(T).Name}");

    public void AddEndpoint(string uri, IEndpoint endpoint)
    {
        _endpoints[EndpointUri.Normalize(ResolvePropertyPlaceholders(uri))] = endpoint;
        StartIfRunning(endpoint);
    }

    public IReadOnlyCollection<IEndpoint> Endpoints => _endpoints.Values.Distinct().ToList();

    private void StartIfRunning(IService service)
    {
        if (IsRunAllowed)
        {
            service.StartAsync().GetAwaiter().GetResult();
        }
    }

    // ----- Routes -----

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

    public IRoute? GetRoute(string routeId)
    {
        lock (_routesLock)
        {
            return _routes.FirstOrDefault(r => r.Id == routeId);
        }
    }

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
                AssignRouteId(definition);
                _routeDefinitions.Add(definition);
            }
        }
    }

    public async Task AddRoutesAsync(IRoutesBuilder builder, CancellationToken cancellationToken = default)
    {
        var definitions = builder.Build(this);
        var created = new List<Route>();
        lock (_routesLock)
        {
            foreach (var definition in definitions)
            {
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

    public async Task StartRouteAsync(string routeId, CancellationToken cancellationToken = default)
    {
        var route = (Route?)GetRoute(routeId) ?? throw new ArgumentException($"No route with id: {routeId}", nameof(routeId));
        await StartRouteInternalAsync(route, cancellationToken).ConfigureAwait(false);
    }

    public async Task StopRouteAsync(string routeId, CancellationToken cancellationToken = default)
    {
        var route = (Route?)GetRoute(routeId) ?? throw new ArgumentException($"No route with id: {routeId}", nameof(routeId));
        await StopRouteInternalAsync(route, cancellationToken).ConfigureAwait(false);
    }

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

        if (route.Status is not (ServiceStatus.Stopped or ServiceStatus.Initialized))
        {
            return false;
        }

        lock (_routesLock)
        {
            _routes.Remove(route);
            _routeDefinitions.Remove(route.Definition);
        }

        EventHelper.Notify(this, CaravanEventType.RouteRemoved, routeId: routeId);
        return true;
    }

    private async Task StartRouteInternalAsync(Route route, CancellationToken cancellationToken)
    {
        try
        {
            await route.StartAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not CaravanException)
        {
            throw new FailedToStartRouteException(route.Id, e.Message, e);
        }

        EventHelper.Notify(this, CaravanEventType.RouteStarted, routeId: route.Id, endpoint: route.Endpoint);
    }

    private async Task StopRouteInternalAsync(Route route, CancellationToken cancellationToken)
    {
        await route.StopAsync(cancellationToken).ConfigureAwait(false);
        EventHelper.Notify(this, CaravanEventType.RouteStopped, routeId: route.Id, endpoint: route.Endpoint);
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
            var routeContext = new RouteContext(this, definition, routeId);
            foreach (var configurer in GetExtension<RouteContextConfigurers>()?.Configurers ?? [])
            {
                configurer(routeContext);
            }

            var endpoint = GetEndpoint(definition.FromUri);
            var pipeline = routeContext.CreateBlockProcessor(definition.Outputs);
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

    private static IEnumerable<Route> OrderForStartup(IEnumerable<Route> routes) =>
        routes.Select((r, i) => (r, i))
            .OrderBy(x => x.r.Definition.StartupOrder ?? int.MaxValue)
            .ThenBy(x => x.i)
            .Select(x => x.r);

    // ----- Lifecycle -----

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

        foreach (var route in OrderForStartup(Routes.Cast<Route>()))
        {
            if (route.Definition.AutoStartup && route.Status != ServiceStatus.Started)
            {
                await StartRouteInternalAsync(route, cancellationToken).ConfigureAwait(false);
            }
        }

        EventHelper.Notify(this, CaravanEventType.ContextStarted);
    }

    protected override async Task DoStopAsync(CancellationToken cancellationToken)
    {
        EventHelper.Notify(this, CaravanEventType.ContextStopping);

        // Stop routes in reverse startup order (consumers first, so inflight exchanges can complete).
        foreach (var route in OrderForStartup(Routes.Cast<Route>()).Reverse())
        {
            await StopRouteInternalAsync(route, cancellationToken).ConfigureAwait(false);
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

        EventHelper.Notify(this, CaravanEventType.ContextStopped);
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _shutdown.Dispose();
        GC.SuppressFinalize(this);
    }

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

    public IProducerTemplate CreateProducerTemplate() => new ProducerTemplate(this);

    public string NextNodeId(string prefix) =>
        prefix + _nodeIdCounters.AddOrUpdate(prefix, 1, (_, n) => n + 1);

    public string ResolvePropertyPlaceholders(string text)
    {
        if (string.IsNullOrEmpty(text) || !text.Contains("{{", StringComparison.Ordinal))
        {
            return text;
        }

        var sb = new StringBuilder();
        var i = 0;
        var depth = 0;
        while (i < text.Length)
        {
            var start = text.IndexOf("{{", i, StringComparison.Ordinal);
            if (start < 0)
            {
                sb.Append(text, i, text.Length - i);
                break;
            }

            var end = text.IndexOf("}}", start + 2, StringComparison.Ordinal);
            if (end < 0)
            {
                sb.Append(text, i, text.Length - i);
                break;
            }

            sb.Append(text, i, start - i);
            var key = text[(start + 2)..end];
            sb.Append(ResolvePlaceholder(key, text));
            i = end + 2;
            if (++depth > 100)
            {
                throw new ArgumentException($"Too many placeholders in: {text}");
            }
        }

        var result = sb.ToString();
        return result.Contains("{{", StringComparison.Ordinal) && result != text ? ResolvePropertyPlaceholders(result) : result;
    }

    private string ResolvePlaceholder(string key, string text)
    {
        string? defaultValue = null;
        var colon = key.IndexOf(':');
        if (colon >= 0)
        {
            defaultValue = key[(colon + 1)..];
            key = key[..colon];
        }

        foreach (var source in PropertiesSources)
        {
            if (source.GetProperty(key) is { } value)
            {
                return value;
            }
        }

        return defaultValue ?? throw new ArgumentException($"Property with key [{key}] not found in properties from text: {text}");
    }

    public T? GetExtension<T>()
        where T : class => _extensions.TryGetValue(typeof(T), out var value) ? (T)value : null;

    public void SetExtension<T>(T extension)
        where T : class => _extensions[typeof(T)] = extension;

    public T GetOrAddExtension<T>(Func<T> factory)
        where T : class => (T)_extensions.GetOrAdd(typeof(T), _ => factory());

    internal ILogger CreateLogger(string category) => LoggerFactory.CreateLogger(category);
}

/// <summary>Callbacks invoked for every route before its processors are built (used by optional features).</summary>
public sealed class RouteContextConfigurers
{
    public List<Action<RouteContext>> Configurers { get; } = [];
}
