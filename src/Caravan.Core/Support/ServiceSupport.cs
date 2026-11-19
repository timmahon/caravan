using Caravan.Spi;

namespace Caravan.Support;

/// <summary>
/// Base class for services. Guarantees that <see cref="DoStartAsync"/> and <see cref="DoStopAsync"/>
/// are called at most once per transition and are never run concurrently.
/// </summary>
public abstract class ServiceSupport : ISuspendableService
{
    private readonly SemaphoreSlim _lock = new(1, 1);

    /// <inheritdoc />
    public ServiceStatus Status { get; private set; } = ServiceStatus.Initialized;

    /// <summary>Whether the service is started.</summary>
    public bool IsStarted => Status == ServiceStatus.Started;

    /// <summary>Whether the service may process work (starting, started or suspended).</summary>
    public bool IsRunAllowed => Status is ServiceStatus.Starting or ServiceStatus.Started or ServiceStatus.Suspended;

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (Status is ServiceStatus.Started or ServiceStatus.Starting)
            {
                return;
            }

            if (Status == ServiceStatus.Suspended)
            {
                Status = ServiceStatus.Starting;
                await DoResumeAsync(cancellationToken).ConfigureAwait(false);
                Status = ServiceStatus.Started;
                return;
            }

            Status = ServiceStatus.Starting;
            try
            {
                await DoStartAsync(cancellationToken).ConfigureAwait(false);
                Status = ServiceStatus.Started;
            }
            catch
            {
                Status = ServiceStatus.Failed;
                throw;
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (Status is ServiceStatus.Stopped or ServiceStatus.Stopping or ServiceStatus.Initialized)
            {
                Status = ServiceStatus.Stopped;
                return;
            }

            Status = ServiceStatus.Stopping;
            try
            {
                await DoStopAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                Status = ServiceStatus.Stopped;
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc />
    public async Task SuspendAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (Status != ServiceStatus.Started)
            {
                return;
            }

            Status = ServiceStatus.Suspending;
            await DoSuspendAsync(cancellationToken).ConfigureAwait(false);
            Status = ServiceStatus.Suspended;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc />
    public Task ResumeAsync(CancellationToken cancellationToken = default) =>
        Status == ServiceStatus.Suspended ? StartAsync(cancellationToken) : Task.CompletedTask;

    /// <summary>Starts the service; called once per start.</summary>
    protected virtual Task DoStartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>Stops the service; called once per stop.</summary>
    protected virtual Task DoStopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>Default suspend behaviour is to stop.</summary>
    protected virtual Task DoSuspendAsync(CancellationToken cancellationToken) => DoStopAsync(cancellationToken);

    /// <summary>Default resume behaviour is to start again.</summary>
    protected virtual Task DoResumeAsync(CancellationToken cancellationToken) => DoStartAsync(cancellationToken);

    /// <summary>Starts a child object if it is a service.</summary>
    public static Task StartServiceAsync(object? service, CancellationToken cancellationToken = default) =>
        service is IService s ? s.StartAsync(cancellationToken) : Task.CompletedTask;

    /// <summary>Stops a child object if it is a service.</summary>
    public static Task StopServiceAsync(object? service, CancellationToken cancellationToken = default) =>
        service is IService s ? s.StopAsync(cancellationToken) : Task.CompletedTask;
}
