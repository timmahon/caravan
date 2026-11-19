namespace Caravan.Health;

/// <summary>
/// A health check (Camel's <c>HealthCheck</c>). Register it with <see cref="HealthCheckRegistry.Register(IHealthCheck)"/>, bind it in the
/// registry, or return it from an <see cref="IHealthCheckRepository"/> (which components, consumers and beans may implement).
/// </summary>
public interface IHealthCheck
{
    /// <summary>The id, unique in the context (e.g. <c>route:orders</c>).</summary>
    string Id { get; }

    /// <summary>The group (<c>context</c>, <c>routes</c>, <c>consumers</c>, <c>circuit-breakers</c>, <c>components</c>, ...).</summary>
    string Group { get; }

    /// <summary>Whether the check tells if the application is ready to take work (default true).</summary>
    bool IsReadiness => true;

    /// <summary>Whether the check tells if the application is alive (default false: only the context check is, by default).</summary>
    bool IsLiveness => false;

    /// <summary>Runs the check.</summary>
    /// <param name="cancellationToken">Cancels the check.</param>
    /// <returns>The result.</returns>
    ValueTask<HealthCheckResult> CheckAsync(CancellationToken cancellationToken = default);
}
