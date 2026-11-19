using Caravan.Spi;

namespace Caravan.Management;

/// <summary>
/// The management API of a context (<see cref="ICaravanContext.Management"/>), Caravan's equivalent of Camel's JMX MBeans: statistics of the
/// context, its routes and their nodes, and operations to control routes and inspect endpoints, components, circuit breakers and inflight
/// exchanges. Statistics live as long as the runtime routes (they are gone once the context stops).
/// </summary>
public interface IManagementService
{
    /// <summary>
    /// Which counters are kept (default <see cref="ManagementStatisticsLevel.Default"/>: context, routes and nodes). Set it before routes are
    /// created.
    /// </summary>
    ManagementStatisticsLevel StatisticsLevel { get; set; }

    /// <summary>The context's statistics.</summary>
    /// <returns>A snapshot.</returns>
    ContextStatistics GetContextStatistics();

    /// <summary>The statistics of every runtime route.</summary>
    /// <returns>Snapshots, in route order.</returns>
    IReadOnlyList<RouteStatistics> GetRouteStatistics();

    /// <summary>The statistics of one route.</summary>
    /// <param name="routeId">The route id.</param>
    /// <returns>A snapshot, or null when there is no such runtime route.</returns>
    RouteStatistics? GetRouteStatistics(string routeId);

    /// <summary>The statistics of the nodes of a route (empty unless <see cref="StatisticsLevel"/> is <see cref="ManagementStatisticsLevel.Default"/>).</summary>
    /// <param name="routeId">The route id.</param>
    /// <returns>Snapshots in the order the nodes were created (depth first).</returns>
    IReadOnlyList<ProcessorStatistics> GetProcessorStatistics(string routeId);

    /// <summary>Resets the counters of the context, every route and every node.</summary>
    void Reset();

    /// <summary>Resets the counters of one route and its nodes.</summary>
    /// <param name="routeId">The route id.</param>
    /// <returns>Whether the route exists.</returns>
    bool ResetRoute(string routeId);

    /// <summary>Starts a route.</summary>
    /// <param name="routeId">The route id.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The operation.</returns>
    Task StartRouteAsync(string routeId, CancellationToken cancellationToken = default);

    /// <summary>Stops a route gracefully.</summary>
    /// <param name="routeId">The route id.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The operation.</returns>
    Task StopRouteAsync(string routeId, CancellationToken cancellationToken = default);

    /// <summary>Suspends a route.</summary>
    /// <param name="routeId">The route id.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The operation.</returns>
    Task SuspendRouteAsync(string routeId, CancellationToken cancellationToken = default);

    /// <summary>Resumes a suspended route.</summary>
    /// <param name="routeId">The route id.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The operation.</returns>
    Task ResumeRouteAsync(string routeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Dumps a route's model as indented text (Camel's <c>dumpRouteAsXml</c>): the <c>from</c> endpoint, then one line per node with its
    /// id and label, nested blocks indented.
    /// </summary>
    /// <param name="routeId">The route id (a route definition that is not created yet is dumped too).</param>
    /// <returns>The text, or null when there is no such route.</returns>
    string? DumpRouteAsText(string routeId);

    /// <summary>The endpoints of the context with their usage.</summary>
    /// <returns>The endpoints, ordered by URI.</returns>
    IReadOnlyList<EndpointInfo> ListEndpoints();

    /// <summary>The components of the context: those created, then those available by scheme.</summary>
    /// <returns>The components, ordered by scheme.</returns>
    IReadOnlyList<ComponentInfo> ListComponents();

    /// <summary>The circuits of the <c>circuitBreaker</c> nodes.</summary>
    /// <returns>The circuits, ordered by id.</returns>
    IReadOnlyList<CircuitBreakerInfo> ListCircuitBreakers();

    /// <summary>The exchanges currently being routed (<see cref="ICaravanContext.InflightRepository"/>).</summary>
    /// <param name="routeId">Only those of this route; null for all.</param>
    /// <param name="limit">The maximum number returned; negative for all.</param>
    /// <param name="sortByLongestDuration">Whether the longest-running come first.</param>
    /// <returns>The inflight exchanges.</returns>
    IReadOnlyList<InflightExchangeInfo> BrowseInflight(string? routeId = null, int limit = -1, bool sortByLongestDuration = false);
}
