using Caravan.Spi;
using Microsoft.Extensions.Logging;

namespace Caravan.Impl;

/// <summary>
/// The default <see cref="IShutdownStrategy"/>:
/// <list type="number">
/// <item>Consumers of routes with <see cref="ShutdownRoute.Default"/> are stopped (or suspended) right away; deferred
/// consumers (<see cref="ShutdownRoute.Defer"/> or <see cref="IShutdownAware.DeferShutdown"/>) are only told to prepare.</item>
/// <item>Waits until no exchange is inflight in the routes and, for routes with
/// <see cref="ShutdownRunningTask.CompleteAllTasks"/>, no message is pending in their consumers.</item>
/// <item>Stops (or suspends) the deferred consumers.</item>
/// <item>When stopping, tells the routes' shutdown-aware services (<see cref="IShutdownAware"/> processors such as the aggregator and
/// the resequencers) to prepare, so they emit what they hold, then waits until they report no pending exchanges and no exchange is
/// inflight.</item>
/// </list>
/// When <see cref="Timeout"/> elapses first, the inflight exchanges are logged, shutdown-aware consumers are forced
/// (they cancel their work) and, on context shutdown, <see cref="ICaravanContext.ShutdownToken"/> is cancelled.
/// </summary>
public sealed class DefaultShutdownStrategy : IShutdownStrategy
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(100);

    /// <inheritdoc />
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(45);

    /// <inheritdoc />
    public bool LogInflightExchangesOnTimeout { get; set; } = true;

    /// <inheritdoc />
    public bool HasTimeoutOccurred { get; private set; }

    /// <inheritdoc />
    public Task<bool> ShutdownAsync(ICaravanContext context, IReadOnlyList<IRoute> routes, TimeSpan? timeout = null, CancellationToken cancellationToken = default) =>
        RunAsync(context, routes, suspendOnly: false, timeout ?? Timeout, cancellationToken);

    /// <inheritdoc />
    public Task<bool> SuspendAsync(ICaravanContext context, IReadOnlyList<IRoute> routes, TimeSpan? timeout = null, CancellationToken cancellationToken = default) =>
        RunAsync(context, routes, suspendOnly: true, timeout ?? Timeout, cancellationToken);

    private async Task<bool> RunAsync(ICaravanContext context, IReadOnlyList<IRoute> routes, bool suspendOnly, TimeSpan timeout, CancellationToken cancellationToken)
    {
        HasTimeoutOccurred = false;
        if (routes.Count == 0)
        {
            return true;
        }

        var logger = context.LoggerFactory.CreateLogger<DefaultShutdownStrategy>();
        using var force = new CancellationTokenSource();
        var work = RunPhasesAsync(context, routes, suspendOnly, logger, force.Token, cancellationToken);
        try
        {
            if (timeout > TimeSpan.Zero)
            {
                await work.WaitAsync(timeout, context.TimeProvider, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await work.WaitAsync(cancellationToken).ConfigureAwait(false);
            }

            return true;
        }
        catch (TimeoutException)
        {
            HasTimeoutOccurred = true;
        }

        var verb = suspendOnly ? "suspend" : "shutdown";
        logger.LogWarning(
            "Timeout occurred during graceful {Verb} of {Count} route(s) after {Timeout}. Forcing the routes to {Verb} now.",
            verb,
            routes.Count,
            timeout,
            verb);
        if (LogInflightExchangesOnTimeout)
        {
            foreach (var info in routes.SelectMany(r => context.InflightRepository.Browse(r.Id)).DistinctBy(i => i.Exchange))
            {
                logger.LogWarning("Inflight exchange {ExchangeId} in route {RouteId} for {Duration}", info.ExchangeId, info.RouteId, info.Duration);
            }
        }

        foreach (var route in routes)
        {
            if (route.Consumer is IShutdownAware aware)
            {
                aware.PrepareShutdown(route.Definition.ShutdownRunningTask, suspendOnly, forced: true);
            }

            foreach (var service in ShutdownAwareServices(route))
            {
                service.PrepareShutdown(route.Definition.ShutdownRunningTask, suspendOnly, forced: true);
            }
        }

        if (!suspendOnly && context is CaravanContext { Status: ServiceStatus.Stopping } caravan)
        {
            await caravan.CancelShutdownTokenAsync().ConfigureAwait(false);
        }

        await force.CancelAsync().ConfigureAwait(false);
        try
        {
            await work.ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(e, "Error during forced {Verb}", verb);
        }

        return false;
    }

    private static async Task RunPhasesAsync(
        ICaravanContext context,
        IReadOnlyList<IRoute> routes,
        bool suspendOnly,
        ILogger logger,
        CancellationToken force,
        CancellationToken cancellationToken)
    {
        // Runs synchronously up to the first consumer that stops asynchronously: consumers are told to prepare
        // (e.g. seda stops taking new messages) before the caller's StopAsync returns its task.
        var deferred = new List<IRoute>();
        foreach (var route in routes)
        {
            if (route.Consumer is not { } consumer)
            {
                continue;
            }

            var runningTask = route.Definition.ShutdownRunningTask;
            var aware = consumer as IShutdownAware;
            aware?.PrepareShutdown(runningTask, suspendOnly, forced: false);
            if (route.Definition.ShutdownRoute == ShutdownRoute.Defer || aware?.DeferShutdown(runningTask) == true)
            {
                deferred.Add(route);
            }
            else
            {
                await StopConsumerAsync(route, consumer, suspendOnly, logger, cancellationToken).ConfigureAwait(false);
            }
        }

        await WaitForInflightAsync(context, routes, suspendOnly, includeServices: false, logger, force, cancellationToken).ConfigureAwait(false);

        foreach (var route in deferred)
        {
            if (route.Consumer is { } consumer)
            {
                await StopConsumerAsync(route, consumer, suspendOnly, logger, cancellationToken).ConfigureAwait(false);
            }
        }

        if (suspendOnly)
        {
            return;
        }

        // No new work arrives any more: the routes' shutdown-aware services (aggregator, resequencer) may now emit what they hold,
        // and the exchanges they send are awaited like inflight ones.
        foreach (var route in routes)
        {
            foreach (var service in ShutdownAwareServices(route))
            {
                try
                {
                    service.PrepareShutdown(route.Definition.ShutdownRunningTask, suspendOnly: false, forced: false);
                }
                catch (Exception e)
                {
                    logger.LogWarning(e, "Error preparing service {Service} of route {RouteId} for shutdown", service, route.Id);
                }
            }
        }

        await WaitForInflightAsync(context, routes, suspendOnly, includeServices: true, logger, force, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The services of a route that take part in shutdown (Camel's <c>ShutdownPrepared</c>/<c>ShutdownAware</c> route services).</summary>
    private static IEnumerable<IShutdownAware> ShutdownAwareServices(IRoute route) =>
        route is Route { RouteContext: var routeContext } ? routeContext.Services.OfType<IShutdownAware>().Distinct() : [];

    private static async Task StopConsumerAsync(IRoute route, IConsumer consumer, bool suspendOnly, ILogger logger, CancellationToken cancellationToken)
    {
        try
        {
            if (suspendOnly && consumer is ISuspendableService suspendable)
            {
                await suspendable.SuspendAsync(cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await consumer.StopAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            logger.LogWarning(e, "Error stopping consumer of route {RouteId}", route.Id);
        }
    }

    private static async Task WaitForInflightAsync(
        ICaravanContext context,
        IReadOnlyList<IRoute> routes,
        bool suspendOnly,
        bool includeServices,
        ILogger logger,
        CancellationToken force,
        CancellationToken cancellationToken)
    {
        var repository = context.InflightRepository as DefaultInflightRepository;
        while (!force.IsCancellationRequested)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var removed = repository?.WaitForRemovalAsync();
            var inflight = 0;
            var pending = 0;
            foreach (var route in routes)
            {
                inflight += route.InflightCount;
                if (!suspendOnly &&
                    route.Definition.ShutdownRunningTask == ShutdownRunningTask.CompleteAllTasks &&
                    route.Consumer is IShutdownAware aware and IService { Status: ServiceStatus.Started })
                {
                    pending += aware.PendingExchangesSize;
                }

                if (includeServices)
                {
                    pending += ShutdownAwareServices(route).Sum(s => s.PendingExchangesSize);
                }
            }

            if (inflight + pending == 0)
            {
                return;
            }

            logger.LogDebug("Waiting as there are still {Inflight} inflight and {Pending} pending exchanges to complete", inflight, pending);

            // Removals signal immediately; the interval is a safety net for pending counts that drop without routing.
            using var wake = CancellationTokenSource.CreateLinkedTokenSource(force, cancellationToken);
            var poll = Task.Delay(PollInterval, TimeProvider.System, wake.Token);
            await (removed is null ? poll : Task.WhenAny(removed, poll)).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            await wake.CancelAsync().ConfigureAwait(false);
        }
    }
}
