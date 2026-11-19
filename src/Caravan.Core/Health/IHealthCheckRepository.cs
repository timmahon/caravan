namespace Caravan.Health;

/// <summary>
/// A source of health checks (Camel's <c>HealthCheckRepository</c>), e.g. one check per route. Components implementing it contribute their
/// checks (the <c>seda</c> component reports full queues); repositories can also be registered with the <see cref="HealthCheckRegistry"/> or
/// bound in the registry.
/// </summary>
public interface IHealthCheckRepository
{
    /// <summary>The repository id.</summary>
    string Id { get; }

    /// <summary>The current checks.</summary>
    /// <returns>The checks.</returns>
    IEnumerable<IHealthCheck> GetHealthChecks();
}
