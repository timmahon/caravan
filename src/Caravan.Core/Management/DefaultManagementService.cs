using System.Runtime.CompilerServices;
using Caravan.Impl;
using Caravan.Observability;
using Caravan.Spi;

namespace Caravan.Management;

/// <summary>The <see cref="IManagementService"/> of a <see cref="CaravanContext"/>.</summary>
internal sealed class DefaultManagementService(CaravanContext context) : IManagementService
{
    private readonly ConditionalWeakTable<IEndpoint, EndpointUsage> _usage = [];

    public ManagementStatisticsLevel StatisticsLevel { get; set; } = ManagementStatisticsLevel.Default;

    /// <summary>The context-level counters (one count per exchange entering the context).</summary>
    public ProcessingCounters ContextCounters { get; } = new();

    /// <summary>Whether context-level counters are kept.</summary>
    public bool ContextStatisticsEnabled => StatisticsLevel != ManagementStatisticsLevel.Off;

    /// <summary>Whether node counters are kept.</summary>
    public bool NodeStatisticsEnabled => StatisticsLevel == ManagementStatisticsLevel.Default;

    public EndpointUsage GetUsage(IEndpoint endpoint) => _usage.GetValue(endpoint, static _ => new EndpointUsage());

    public ContextStatistics GetContextStatistics()
    {
        var routes = context.Routes;
        return new ContextStatistics
        {
            Name = context.Name,
            Status = context.Status,
            Uptime = Uptime(context.StartedAt),
            Routes = routes.Count,
            StartedRoutes = routes.Count(r => r.Status == ServiceStatus.Started),
            Statistics = ContextCounters.Snapshot(),
        };
    }

    public IReadOnlyList<RouteStatistics> GetRouteStatistics() => [.. context.Routes.OfType<Route>().Select(Snapshot)];

    public RouteStatistics? GetRouteStatistics(string routeId) => context.GetRoute(routeId) is Route route ? Snapshot(route) : null;

    public IReadOnlyList<ProcessorStatistics> GetProcessorStatistics(string routeId)
    {
        if (context.GetRoute(routeId) is not Route route)
        {
            return [];
        }

        var positions = new Dictionary<Model.ProcessorDefinition, int>(ReferenceEqualityComparer.Instance);
        foreach (var node in route.Definition.AllNodes())
        {
            positions.TryAdd(node, positions.Count);
        }

        return
        [
            .. route.RouteContext.ProcessorCounters.Values
                .OrderBy(p => positions.TryGetValue(p.Definition, out var index) ? index : int.MaxValue)
                .ThenBy(p => p.Order)
                .Select(p => new ProcessorStatistics
            {
                RouteId = route.Id,
                NodeId = p.NodeId,
                ShortName = p.Definition.ShortName,
                Label = p.Definition.Label,
                Level = p.Level,
                Statistics = p.Counters.Snapshot(),
            }),
        ];
    }

    public void Reset()
    {
        var now = context.TimeProvider.GetUtcNow();
        ContextCounters.Reset(now);
        foreach (var route in context.Routes.OfType<Route>())
        {
            ResetRoute(route, now);
        }

        foreach (var endpoint in context.Endpoints)
        {
            if (_usage.TryGetValue(endpoint, out var usage))
            {
                usage.Reset();
            }
        }
    }

    public bool ResetRoute(string routeId)
    {
        if (context.GetRoute(routeId) is not Route route)
        {
            return false;
        }

        ResetRoute(route, context.TimeProvider.GetUtcNow());
        return true;
    }

    public Task StartRouteAsync(string routeId, CancellationToken cancellationToken = default) => context.StartRouteAsync(routeId, cancellationToken);

    public Task StopRouteAsync(string routeId, CancellationToken cancellationToken = default) => context.StopRouteAsync(routeId, cancellationToken);

    public Task SuspendRouteAsync(string routeId, CancellationToken cancellationToken = default) => context.SuspendRouteAsync(routeId, cancellationToken);

    public Task ResumeRouteAsync(string routeId, CancellationToken cancellationToken = default) => context.ResumeRouteAsync(routeId, cancellationToken);

    public string? DumpRouteAsText(string routeId)
    {
        var definition = context.GetRoute(routeId)?.Definition ?? context.RouteDefinitions.FirstOrDefault(d => d.Id == routeId);
        return definition is null ? null : RouteModelDumper.Dump(definition);
    }

    public IReadOnlyList<EndpointInfo> ListEndpoints()
    {
        var routes = context.Routes;
        return
        [
            .. context.Endpoints.Select(e =>
            {
                _usage.TryGetValue(e, out var usage);
                return new EndpointInfo
                {
                    Uri = UriSanitizer.Sanitize(e.EndpointUri)!,
                    Scheme = Support.EndpointUri.Parse(e.EndpointUri).Scheme,
                    Singleton = e.IsSingleton,
                    Status = e.Status,
                    InboundHits = usage?.Inbound ?? 0,
                    OutboundHits = usage?.Outbound ?? 0,
                    ConsumingRoutes = [.. routes.Where(r => ReferenceEquals(r.Endpoint, e)).Select(r => r.Id)],
                };
            }).OrderBy(e => e.Uri, StringComparer.Ordinal),
        ];
    }

    public IReadOnlyList<ComponentInfo> ListComponents() =>
    [
        .. context.ComponentNames.Select(scheme =>
        {
            var created = context.FindCreatedComponent(scheme);
            return new ComponentInfo(scheme, created?.GetType().FullName, created?.Status);
        }).OrderBy(c => c.Scheme, StringComparer.OrdinalIgnoreCase),
    ];

    public IReadOnlyList<CircuitBreakerInfo> ListCircuitBreakers()
    {
        var registry = context.GetCircuitBreakerStateRegistry();
        return
        [
            .. registry.Ids.Select(id => registry.GetState(id) is { } state ? new CircuitBreakerInfo(id, state) : null)
                .OfType<CircuitBreakerInfo>()
                .OrderBy(c => c.Id, StringComparer.Ordinal),
        ];
    }

    public IReadOnlyList<InflightExchangeInfo> BrowseInflight(string? routeId = null, int limit = -1, bool sortByLongestDuration = false) =>
        context.InflightRepository.Browse(routeId, limit, sortByLongestDuration);

    private static void ResetRoute(Route route, DateTimeOffset now)
    {
        route.RouteContext.Counters.Reset(now);
        foreach (var node in route.RouteContext.ProcessorCounters.Values)
        {
            node.Counters.Reset(now);
        }
    }

    private RouteStatistics Snapshot(Route route) => new()
    {
        RouteId = route.Id,
        Group = route.Definition.Group,
        Description = route.Definition.Description,
        EndpointUri = UriSanitizer.Sanitize(route.Endpoint.EndpointUri)!,
        Status = route.Status,
        Uptime = route.Status == ServiceStatus.Started ? Uptime(route.StartedAt) : TimeSpan.Zero,
        TemplateId = route.Definition.TemplateId,
        Statistics = route.RouteContext.Counters.Snapshot(),
    };

    private TimeSpan Uptime(DateTimeOffset? started) =>
        started is { } s ? context.TimeProvider.GetUtcNow() - s : TimeSpan.Zero;
}
