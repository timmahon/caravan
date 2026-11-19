using System.Collections.Concurrent;
using Caravan.Processors;
using Caravan.Spi;
using Microsoft.Extensions.Logging;

namespace Caravan.Impl;

/// <summary>
/// Supervises route startup (Camel's <c>SupervisingRouteController</c>): a route that fails to start does not fail
/// the context; it is retried in the background with backoff until it starts or the attempts are exhausted.
/// Enable it with <see cref="ICaravanContext.SupervisingRouteController"/> before starting the context.
/// The routes it supervises have status <see cref="ServiceStatus.Failed"/> until they start; see <see cref="FailedRoutes"/>.
/// </summary>
public sealed class SupervisingRouteController
{
    private readonly ConcurrentDictionary<string, RouteRestartState> _states = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _loops = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<Task, byte> _tasks = new();

    /// <summary>Delay before the routes are started after the context started (default 0: routes start with the context).</summary>
    public TimeSpan InitialDelay { get; set; } = TimeSpan.Zero;

    /// <summary>Delay before the first restart attempt (default 2s).</summary>
    public TimeSpan BackOffDelay { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>Upper bound for the delay between attempts when <see cref="BackOffMultiplier"/> grows it (zero = unbounded).</summary>
    public TimeSpan BackOffMaxDelay { get; set; } = TimeSpan.Zero;

    /// <summary>Factor applied to the delay after each failed attempt (default 1: fixed delay).</summary>
    public double BackOffMultiplier { get; set; } = 1.0;

    /// <summary>Maximum number of restart attempts per route (default 0: unlimited).</summary>
    public int BackOffMaxAttempts { get; set; }

    /// <summary>The routes that failed to start and are being retried or exhausted.</summary>
    public IReadOnlyCollection<RouteRestartState> FailedRoutes => _states.Values.ToList();

    /// <summary>The ids of the routes whose attempts are exhausted.</summary>
    public IReadOnlyCollection<string> ExhaustedRouteIds => _states.Values.Where(s => s.IsExhausted).Select(s => s.RouteId).ToList();

    /// <summary>The restart state of a route, or null when it is not failing.</summary>
    public RouteRestartState? GetRestartState(string routeId) => _states.GetValueOrDefault(routeId);

    /// <summary>Starts the routes in order, supervising failures; with an initial delay this happens in the background.</summary>
    internal Task StartRoutesAsync(CaravanContext context, IReadOnlyList<Route> routes, CancellationToken shutdown)
    {
        if (InitialDelay <= TimeSpan.Zero)
        {
            return StartAllAsync(context, routes, shutdown);
        }

        Track(Task.Run(
            async () =>
            {
                await Task.Delay(InitialDelay, context.TimeProvider, shutdown).ConfigureAwait(false);
                await StartAllAsync(context, routes, shutdown).ConfigureAwait(false);
            },
            CancellationToken.None));
        return Task.CompletedTask;
    }

    /// <summary>Stops supervising a route (it was stopped or removed by the application).</summary>
    internal void Forget(string routeId)
    {
        if (_loops.TryRemove(routeId, out var cts))
        {
            cts.Cancel();
        }

        _states.TryRemove(routeId, out _);
    }

    /// <summary>Cancels all restart loops and waits for them.</summary>
    internal async Task StopAsync()
    {
        foreach (var id in _loops.Keys.ToList())
        {
            if (_loops.TryRemove(id, out var cts))
            {
                await cts.CancelAsync().ConfigureAwait(false);
            }
        }

        await Task.WhenAll(_tasks.Keys).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        _states.Clear();
    }

    private async Task StartAllAsync(CaravanContext context, IReadOnlyList<Route> routes, CancellationToken shutdown)
    {
        foreach (var route in routes)
        {
            if (shutdown.IsCancellationRequested)
            {
                return;
            }

            try
            {
                await context.StartRouteInternalAsync(route, shutdown).ConfigureAwait(false);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                context.CreateLogger(nameof(SupervisingRouteController))
                    .LogWarning(e, "Route {RouteId} failed to start; it will be retried in {Delay}", route.Id, BackOffDelay);
                Supervise(context, route, e, shutdown);
            }
        }
    }

    private void Supervise(CaravanContext context, Route route, Exception error, CancellationToken shutdown)
    {
        var state = new RouteRestartState(route.Id) { LastError = error };
        _states[route.Id] = state;
        var cts = CancellationTokenSource.CreateLinkedTokenSource(shutdown);
        if (_loops.TryRemove(route.Id, out var previous))
        {
            previous.Cancel();
        }

        _loops[route.Id] = cts;
        Track(Task.Run(() => RestartLoopAsync(context, route, state, cts.Token), CancellationToken.None));
    }

    private async Task RestartLoopAsync(CaravanContext context, Route route, RouteRestartState state, CancellationToken cancellationToken)
    {
        var logger = context.CreateLogger(nameof(SupervisingRouteController));
        var delay = BackOffDelay;
        while (!cancellationToken.IsCancellationRequested)
        {
            if (BackOffMaxAttempts > 0 && state.Attempts >= BackOffMaxAttempts)
            {
                state.IsExhausted = true;
                state.NextAttempt = null;
                logger.LogError(state.LastError, "Route {RouteId} could not be started after {Attempts} attempts", route.Id, state.Attempts);
                return;
            }

            state.NextAttempt = context.TimeProvider.GetUtcNow() + delay;
            try
            {
                await Task.Delay(delay, context.TimeProvider, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            state.Attempts++;
            EventHelper.Notify(context, new CaravanEvent(CaravanEventType.RouteRestarting, context) { RouteId = route.Id, Attempt = state.Attempts });
            try
            {
                await context.StartRouteInternalAsync(route, cancellationToken).ConfigureAwait(false);
                _states.TryRemove(route.Id, out _);
                _loops.TryRemove(route.Id, out _);
                logger.LogInformation("Route {RouteId} started after {Attempts} attempts", route.Id, state.Attempts);
                return;
            }
            catch (Exception e) when (!cancellationToken.IsCancellationRequested)
            {
                state.LastError = e;
                EventHelper.Notify(
                    context,
                    new CaravanEvent(CaravanEventType.RouteRestartingFailure, context) { RouteId = route.Id, Attempt = state.Attempts, Cause = e });
                delay = NextDelay(delay);
            }
            catch (Exception)
            {
                return;
            }
        }
    }

    private TimeSpan NextDelay(TimeSpan delay)
    {
        if (BackOffMultiplier <= 1.0)
        {
            return delay;
        }

        var next = delay * BackOffMultiplier;
        return BackOffMaxDelay > TimeSpan.Zero && next > BackOffMaxDelay ? BackOffMaxDelay : next;
    }

    private void Track(Task task)
    {
        _tasks.TryAdd(task, 0);
        task.ContinueWith(t => _tasks.TryRemove(t, out _), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }
}
