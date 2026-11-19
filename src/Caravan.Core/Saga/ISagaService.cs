namespace Caravan.Saga;

/// <summary>
/// Creates and finds sagas (Camel's <c>CamelSagaService</c>). The default is <see cref="InMemorySagaService"/>; set another with
/// <see cref="SagaContextExtensions.SetSagaService"/> or bind exactly one in the registry. A service that is an <see cref="Spi.IService"/> is
/// added to the context's services (started and stopped with it) by the first route that uses the <c>saga</c> EIP.
/// </summary>
public interface ISagaService
{
    /// <summary>Starts a new saga.</summary>
    /// <param name="exchange">The exchange that starts it.</param>
    /// <returns>The coordinator of the new saga.</returns>
    Task<ISagaCoordinator> NewSagaAsync(Exchange exchange);

    /// <summary>Finds a running (not yet completed or compensated) saga.</summary>
    /// <param name="id">The saga id (the <see cref="CaravanHeaders.SagaLongRunningAction"/> header).</param>
    /// <returns>The coordinator, or null when the saga is unknown or finished.</returns>
    Task<ISagaCoordinator?> GetSagaAsync(string id);
}
