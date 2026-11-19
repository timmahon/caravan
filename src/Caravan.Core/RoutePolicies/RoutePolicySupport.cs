using Caravan.Spi;
using Microsoft.Extensions.Logging;

namespace Caravan.RoutePolicies;

/// <summary>
/// Base class of route policies (Camel's <c>RoutePolicySupport</c>): suspends and resumes a route's consumer. The operations run in the
/// background, one after the other, because a callback such as <see cref="IRoutePolicy.OnExchangeDone"/> runs inside the consumer's own work,
/// which a suspension waits for. The route itself stays started (its status does not change), as in Camel.
/// </summary>
public abstract class RoutePolicySupport : IRoutePolicy
{
    private readonly Lock _gate = new();
    private Task _pending = Task.CompletedTask;

    /// <summary>The last consumer operation started (completed when all are done); mainly for tests.</summary>
    public Task PendingOperation
    {
        get
        {
            lock (_gate)
            {
                return _pending;
            }
        }
    }

    /// <inheritdoc />
    public virtual void OnInit(IRoute route)
    {
    }

    /// <inheritdoc />
    public virtual void OnRemove(IRoute route)
    {
    }

    /// <inheritdoc />
    public virtual void OnStart(IRoute route)
    {
    }

    /// <inheritdoc />
    public virtual void OnStop(IRoute route)
    {
    }

    /// <inheritdoc />
    public virtual void OnSuspend(IRoute route)
    {
    }

    /// <inheritdoc />
    public virtual void OnResume(IRoute route)
    {
    }

    /// <inheritdoc />
    public virtual void OnExchangeBegin(IRoute route, Exchange exchange)
    {
    }

    /// <inheritdoc />
    public virtual void OnExchangeDone(IRoute route, Exchange exchange)
    {
    }

    /// <summary>Suspends the consumer (stopped when it cannot be suspended) in the background.</summary>
    /// <param name="route">The route whose consumer to suspend.</param>
    /// <returns>The operation.</returns>
    protected Task SuspendConsumerAsync(IRoute route) => Enqueue(route, async consumer =>
    {
        if (consumer is ISuspendableService { Status: ServiceStatus.Started } suspendable)
        {
            await suspendable.SuspendAsync().ConfigureAwait(false);
        }
    });

    /// <summary>Resumes (or starts) the consumer in the background.</summary>
    /// <param name="route">The route whose consumer to resume.</param>
    /// <returns>The operation.</returns>
    protected Task ResumeConsumerAsync(IRoute route) => Enqueue(route, async consumer =>
    {
        if (consumer is ISuspendableService { Status: ServiceStatus.Suspended } suspendable)
        {
            await suspendable.ResumeAsync().ConfigureAwait(false);
        }
    });

    /// <summary>Whether the route's consumer is currently suspended.</summary>
    /// <param name="route">The route.</param>
    /// <returns>True when suspended (or suspending).</returns>
    protected static bool IsConsumerSuspended(IRoute route) =>
        route.Consumer is IService { Status: ServiceStatus.Suspended or ServiceStatus.Suspending };

    private Task Enqueue(IRoute route, Func<IConsumer, Task> operation)
    {
        lock (_gate)
        {
            _pending = _pending.ContinueWith(
                async _ =>
                {
                    if (route.Consumer is not { } consumer)
                    {
                        return;
                    }

                    try
                    {
                        await operation(consumer).ConfigureAwait(false);
                    }
                    catch (Exception e)
                    {
                        route.Endpoint.Context.LoggerFactory.CreateLogger(GetType()).LogWarning(e, "Route policy failed to change the consumer of route {RouteId}", route.Id);
                    }
                },
                CancellationToken.None,
                TaskContinuationOptions.None,
                TaskScheduler.Default).Unwrap();
            return _pending;
        }
    }
}
