using Caravan.Spi;

namespace Caravan.Support;

/// <summary>Resolves variable names to their repository (Camel's <c>ExchangeHelper</c> variable methods).</summary>
internal static class VariableHelper
{
    /// <summary>
    /// Splits <paramref name="name"/> into its repository and the name inside it: no prefix is an exchange variable (null repository),
    /// <c>route:x</c> is <c>x</c> of the exchange's current route (stored as <c>routeId:x</c>), <c>id:x</c> is <c>x</c> in the repository
    /// <c>id</c>.
    /// </summary>
    /// <exception cref="ArgumentException">The prefix names no repository, or a route variable is used outside a route.</exception>
    public static (IVariableRepository? Repository, string Name) Resolve(ICaravanContext context, Exchange? exchange, string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        var colon = name.IndexOf(':', StringComparison.Ordinal);
        if (colon <= 0)
        {
            return (null, name);
        }

        var id = name[..colon];
        var rest = name[(colon + 1)..];
        var repository = context.VariableRepositories.Find(id)
            ?? throw new ArgumentException($"VariableRepository with id: {id} does not exist (variable {name})");
        if (id == VariableRepositoryRegistry.RouteId && !rest.Contains(':', StringComparison.Ordinal))
        {
            var routeId = exchange?.CurrentRouteId ?? exchange?.FromRouteId
                ?? throw new ArgumentException($"The route variable {name} needs a route id (route:routeId:name) outside a route");
            rest = routeId + ":" + rest;
        }

        return (repository, rest);
    }

    /// <summary>Gets a variable of a repository or the exchange.</summary>
    public static object? Get(ICaravanContext context, Exchange? exchange, string name)
    {
        var (repository, key) = Resolve(context, exchange, name);
        if (repository is not null)
        {
            return repository.GetVariable(key);
        }

        return exchange?.TryGetExchangeVariable(key);
    }

    /// <summary>Removes the variables matching <paramref name="pattern"/> (a name, <c>*</c>, a wildcard or a regular expression); returns whether one existed.</summary>
    public static bool Remove(ICaravanContext context, Exchange? exchange, string pattern)
    {
        var (repository, key) = Resolve(context, exchange, pattern);
        var names = repository is not null ? repository.Variables.Keys : (IEnumerable<string>?)exchange?.ExchangeVariableNames ?? [];
        var removed = false;
        foreach (var candidate in names.ToList())
        {
            if (candidate == key || PatternHelper.Matches(candidate, key))
            {
                removed = true;
                if (repository is not null)
                {
                    repository.RemoveVariable(candidate);
                }
                else
                {
                    exchange!.RemoveExchangeVariable(candidate);
                }
            }
        }

        return removed;
    }
}
