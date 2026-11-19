namespace Caravan.Spi;

/// <summary>
/// A consumer that takes part in graceful shutdown (Camel's <c>ShutdownAware</c> and <c>ShutdownPrepared</c>),
/// typically because it queues messages (e.g. <c>seda</c>) or runs its own loop (timers, scheduled polls).
/// </summary>
public interface IShutdownAware
{
    /// <summary>Whether the consumer should be stopped after the other routes completed their inflight exchanges.</summary>
    bool DeferShutdown(ShutdownRunningTask runningTask);

    /// <summary>Messages accepted but not yet processed (counted while waiting when the route uses <see cref="ShutdownRunningTask.CompleteAllTasks"/>).</summary>
    int PendingExchangesSize { get; }

    /// <summary>Called before the consumer is stopped or suspended, and again with <paramref name="forced"/> when the shutdown timed out.</summary>
    /// <param name="runningTask">The route's <see cref="ShutdownRunningTask"/>.</param>
    /// <param name="suspendOnly">True when the route is being suspended rather than stopped.</param>
    /// <param name="forced">True when the timeout elapsed: stop waiting for work and cancel it.</param>
    void PrepareShutdown(ShutdownRunningTask runningTask, bool suspendOnly, bool forced);
}
