namespace Caravan.Spi;

/// <summary>A component with a start/stop lifecycle (Camel's <c>Service</c>).</summary>
public interface IService
{
    /// <summary>The current lifecycle status.</summary>
    ServiceStatus Status { get; }

    /// <summary>Starts the service; does nothing when it is already started.</summary>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>Stops the service; does nothing when it is not started.</summary>
    Task StopAsync(CancellationToken cancellationToken = default);
}
