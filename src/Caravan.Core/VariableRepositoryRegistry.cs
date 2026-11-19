using System.Collections.Concurrent;
using Caravan.Impl;
using Caravan.Spi;

namespace Caravan;

/// <summary>
/// The variable repositories of a context (<see cref="ICaravanContext.VariableRepositories"/>): <see cref="Global"/> (variables named
/// <c>global:name</c>), <see cref="Route"/> (<c>route:name</c> for the current route, <c>route:routeId:name</c> for another one) and custom
/// repositories, added with <see cref="Add"/> or bound in the registry under their id. Variables without prefix belong to the exchange
/// (<see cref="Exchange.Variables"/>).
/// </summary>
public sealed class VariableRepositoryRegistry
{
    /// <summary>The id of the global repository.</summary>
    public const string GlobalId = "global";

    /// <summary>The id of the route repository.</summary>
    public const string RouteId = "route";

    private readonly ICaravanContext _context;
    private readonly ConcurrentDictionary<string, IVariableRepository> _repositories = new(StringComparer.Ordinal);

    /// <summary>Creates the registry of <paramref name="context"/>.</summary>
    /// <param name="context">The context.</param>
    public VariableRepositoryRegistry(ICaravanContext context)
    {
        _context = context;
        Add(new InMemoryVariableRepository(GlobalId));
        Add(new InMemoryVariableRepository(RouteId));
    }

    /// <summary>The context-wide variables (<c>global:name</c>).</summary>
    public IVariableRepository Global => _repositories[GlobalId];

    /// <summary>The route variables, stored under <c>routeId:name</c> (<c>route:name</c> / <c>route:routeId:name</c>).</summary>
    public IVariableRepository Route => _repositories[RouteId];

    /// <summary>Adds (or replaces) a repository; its id becomes a variable name prefix.</summary>
    /// <param name="repository">The repository.</param>
    public void Add(IVariableRepository repository)
    {
        ArgumentNullException.ThrowIfNull(repository);
        _repositories[repository.Id] = repository;
    }

    /// <summary>Finds the repository with id <paramref name="id"/>: an added one, else an <see cref="IVariableRepository"/> bean of that name.</summary>
    /// <param name="id">The repository id.</param>
    /// <returns>The repository, or null.</returns>
    public IVariableRepository? Find(string id) =>
        _repositories.TryGetValue(id, out var repository) ? repository : _context.Registry.Lookup(id) as IVariableRepository;
}
