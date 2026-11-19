namespace Caravan.Spi;

/// <summary>
/// Stops or suspends routes gracefully (Camel's <c>ShutdownStrategy</c>): consumers are stopped first so no new work
/// arrives, deferred consumers (<see cref="ShutdownRoute.Defer"/>, <see cref="IShutdownAware"/>) keep running, then
/// inflight exchanges are awaited up to <see cref="Timeout"/>, after which the shutdown is forced.
/// </summary>
public interface IShutdownStrategy
{
    /// <summary>How long to wait for inflight exchanges (default 45s).</summary>
    TimeSpan Timeout { get; set; }

    /// <summary>Whether to log the inflight exchanges when the timeout elapses (default true).</summary>
    bool LogInflightExchangesOnTimeout { get; set; }

    /// <summary>Whether the last shutdown or suspend timed out.</summary>
    bool HasTimeoutOccurred { get; }

    /// <summary>
    /// Stops the consumers of <paramref name="routes"/> gracefully. The routes themselves (their processors) are stopped
    /// by the caller afterwards. Returns false when the timeout elapsed.
    /// </summary>
    /// <param name="context">The context.</param>
    /// <param name="routes">The routes, in the order they should stop.</param>
    /// <param name="timeout">Overrides <see cref="Timeout"/>.</param>
    /// <param name="cancellationToken">Cancels waiting.</param>
    Task<bool> ShutdownAsync(ICaravanContext context, IReadOnlyList<IRoute> routes, TimeSpan? timeout = null, CancellationToken cancellationToken = default);

    /// <summary>Suspends the consumers of <paramref name="routes"/> gracefully. Returns false when the timeout elapsed.</summary>
    /// <param name="context">The context.</param>
    /// <param name="routes">The routes, in the order they should be suspended.</param>
    /// <param name="timeout">Overrides <see cref="Timeout"/>.</param>
    /// <param name="cancellationToken">Cancels waiting.</param>
    Task<bool> SuspendAsync(ICaravanContext context, IReadOnlyList<IRoute> routes, TimeSpan? timeout = null, CancellationToken cancellationToken = default);
}
