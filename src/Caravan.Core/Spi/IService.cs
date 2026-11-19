namespace Caravan.Spi;

/// <summary>Lifecycle state of a <see cref="IService"/>.</summary>
public enum ServiceStatus
{
    Initialized,
    Starting,
    Started,
    Suspending,
    Suspended,
    Stopping,
    Stopped,
    Failed,
}

/// <summary>A component with a start/stop lifecycle (Camel's <c>Service</c>).</summary>
public interface IService
{
    ServiceStatus Status { get; }

    Task StartAsync(CancellationToken cancellationToken = default);

    Task StopAsync(CancellationToken cancellationToken = default);
}

/// <summary>A service that can be suspended and resumed without being fully stopped.</summary>
public interface ISuspendableService : IService
{
    Task SuspendAsync(CancellationToken cancellationToken = default);

    Task ResumeAsync(CancellationToken cancellationToken = default);
}
