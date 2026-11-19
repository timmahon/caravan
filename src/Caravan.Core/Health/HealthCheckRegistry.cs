using Caravan.Spi;
using Caravan.Support;

namespace Caravan.Health;

/// <summary>
/// The health checks of a context (<see cref="ICaravanContext.HealthChecks"/>, Camel's <c>HealthCheckRegistry</c>): the built-in checks
/// (context, routes, consumers, circuit breakers), checks and repositories registered here or bound in the registry, and those of components
/// that implement <see cref="IHealthCheckRepository"/>.
/// </summary>
public sealed class HealthCheckRegistry
{
    private readonly ICaravanContext _context;
    private readonly CopyOnWriteList<IHealthCheck> _checks = new();
    private readonly CopyOnWriteList<IHealthCheckRepository> _repositories = new();

    /// <summary>Creates the registry of <paramref name="context"/> with the built-in checks.</summary>
    /// <param name="context">The context.</param>
    public HealthCheckRegistry(ICaravanContext context)
    {
        _context = context;
        _checks.Add(new ContextHealthCheck(context));
        _checks.Add(new CircuitBreakersHealthCheck(context));
        _repositories.Add(new RoutesHealthCheckRepository(context));
        _repositories.Add(new ConsumersHealthCheckRepository(context));
    }

    /// <summary>Whether checks run (default true); when false <see cref="CheckAsync"/> returns nothing.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Comma-separated patterns (exact, wildcard, regular expression) of check ids or groups to leave out (Camel's <c>excludePattern</c>).</summary>
    public string? ExcludePattern { get; set; }

    /// <summary>Registers a check (does nothing when it is registered already).</summary>
    /// <param name="check">The check.</param>
    public void Register(IHealthCheck check)
    {
        ArgumentNullException.ThrowIfNull(check);
        if (!_checks.Contains(check))
        {
            _checks.Add(check);
        }
    }

    /// <summary>Registers a repository of checks (does nothing when it is registered already).</summary>
    /// <param name="repository">The repository.</param>
    public void Register(IHealthCheckRepository repository)
    {
        ArgumentNullException.ThrowIfNull(repository);
        if (!_repositories.Contains(repository))
        {
            _repositories.Add(repository);
        }
    }

    /// <summary>Removes a check.</summary>
    /// <param name="check">The check.</param>
    /// <returns>Whether it was registered.</returns>
    public bool Unregister(IHealthCheck check) => _checks.Remove(check);

    /// <summary>Removes a repository.</summary>
    /// <param name="repository">The repository.</param>
    /// <returns>Whether it was registered.</returns>
    public bool Unregister(IHealthCheckRepository repository) => _repositories.Remove(repository);

    /// <summary>The current checks (not excluded), ordered by id.</summary>
    /// <returns>The checks.</returns>
    public IReadOnlyList<IHealthCheck> GetHealthChecks()
    {
        var repositories = _repositories
            .Concat(_context.Registry.FindByType<IHealthCheckRepository>().Values)
            .Concat(_context.ComponentNames.Select(FindComponent).OfType<IHealthCheckRepository>())
            .Distinct(ReferenceEqualityComparer.Instance)
            .Cast<IHealthCheckRepository>();
        return
        [
            .. _checks
                .Concat(_context.Registry.FindByType<IHealthCheck>().Values)
                .Concat(repositories.SelectMany(r => r.GetHealthChecks()))
                .Distinct(ReferenceEqualityComparer.Instance)
                .Cast<IHealthCheck>()
                .Where(c => !IsExcluded(c))
                .OrderBy(c => c.Id, StringComparer.Ordinal),
        ];
    }

    /// <summary>Runs the checks matching <paramref name="filter"/> (all when null); a check that throws is <see cref="HealthState.Down"/>.</summary>
    /// <param name="filter">Selects the checks.</param>
    /// <param name="cancellationToken">Cancels the checks.</param>
    /// <returns>The results, ordered by id.</returns>
    public async Task<IReadOnlyList<HealthCheckResult>> CheckAsync(Func<IHealthCheck, bool>? filter = null, CancellationToken cancellationToken = default)
    {
        if (!Enabled)
        {
            return [];
        }

        var results = new List<HealthCheckResult>();
        foreach (var check in GetHealthChecks())
        {
            if (filter is not null && !filter(check))
            {
                continue;
            }

            HealthCheckResult result;
            try
            {
                result = await check.CheckAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e) when (e is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                result = new HealthCheckResult(check.Id, HealthState.Down) { Message = e.Message, Error = e };
            }

            results.Add(result with { Group = result.Group ?? check.Group });
        }

        return results;
    }

    /// <summary>Runs the readiness checks.</summary>
    /// <param name="cancellationToken">Cancels the checks.</param>
    /// <returns>The results.</returns>
    public Task<IReadOnlyList<HealthCheckResult>> CheckReadinessAsync(CancellationToken cancellationToken = default) =>
        CheckAsync(c => c.IsReadiness, cancellationToken);

    /// <summary>Runs the liveness checks.</summary>
    /// <param name="cancellationToken">Cancels the checks.</param>
    /// <returns>The results.</returns>
    public Task<IReadOnlyList<HealthCheckResult>> CheckLivenessAsync(CancellationToken cancellationToken = default) =>
        CheckAsync(c => c.IsLiveness, cancellationToken);

    /// <summary>The worst state of <paramref name="results"/>: Down, then Degraded/Unknown, else Up.</summary>
    /// <param name="results">The results.</param>
    /// <returns>The aggregated state (Up when empty).</returns>
    public static HealthState Aggregate(IEnumerable<HealthCheckResult> results)
    {
        var state = HealthState.Up;
        foreach (var result in results)
        {
            if (result.State == HealthState.Down)
            {
                return HealthState.Down;
            }

            if (result.State is HealthState.Degraded or HealthState.Unknown)
            {
                state = HealthState.Degraded;
            }
        }

        return state;
    }

    private IComponent? FindComponent(string scheme) => (_context as CaravanContext)?.FindCreatedComponent(scheme);

    private bool IsExcluded(IHealthCheck check)
    {
        if (ExcludePattern is not { } patterns)
        {
            return false;
        }

        foreach (var pattern in patterns.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (PatternHelper.Matches(check.Id, pattern) || PatternHelper.Matches(check.Group, pattern))
            {
                return true;
            }
        }

        return false;
    }
}
