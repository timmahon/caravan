namespace Caravan.Processors;

/// <summary>
/// Picks the <c>onException</c> clause for an exception, like Camel's <c>DefaultExceptionPolicyStrategy</c>: route-scoped clauses
/// are checked before global ones; the cause chain is walked from the innermost exception outwards; an exact type match ends the
/// search, otherwise the clause whose type is closest to the thrown type in the hierarchy wins (the first found on a tie).
/// </summary>
internal static class ExceptionPolicyStrategy
{
    public static ExceptionPolicy? Find(IReadOnlyList<ExceptionPolicy> policies, Exchange exchange, Exception exception)
    {
        if (policies.Count == 0)
        {
            return null;
        }

        var chain = CauseChainFromInnermost(exception);
        var candidates = new SortedDictionary<int, ExceptionPolicy>();
        var exactMatch = false;
        foreach (var routeScoped in (bool[])[true, false])
        {
            var scoped = policies.Where(p => p.Definition.RouteScoped == routeScoped).ToList();
            foreach (var e in chain)
            {
                if (exactMatch)
                {
                    break;
                }

                exactMatch = FindMatch(scoped, exchange, e, candidates);
            }
        }

        return candidates.Count == 0 ? null : candidates.First().Value;
    }

    /// <summary>The exception and its inner exceptions, innermost first.</summary>
    public static List<Exception> CauseChainFromInnermost(Exception exception)
    {
        var chain = new List<Exception>();
        for (var e = exception; e is not null && chain.Count < 100 && !chain.Contains(e); e = e.InnerException)
        {
            chain.Add(e);
        }

        chain.Reverse();
        return chain;
    }

    private static bool FindMatch(List<ExceptionPolicy> policies, Exchange exchange, Exception exception, SortedDictionary<int, ExceptionPolicy> candidates)
    {
        var thrownType = exception.GetType();
        var targetLevel = InheritanceLevel(thrownType);
        ExceptionPolicy? candidate = null;
        var candidateDiff = int.MaxValue;
        foreach (var policy in policies)
        {
            bool? matchesWhen = null;
            foreach (var type in policy.Definition.ExceptionTypes)
            {
                if (!type.IsInstanceOfType(exception))
                {
                    continue;
                }

                matchesWhen ??= policy.Matches(exchange);
                if (matchesWhen != true)
                {
                    break;
                }

                if (type == thrownType)
                {
                    candidates.TryAdd(0, policy);
                    return true;
                }

                var diff = targetLevel - InheritanceLevel(type);
                if (diff < candidateDiff)
                {
                    candidate = policy;
                    candidateDiff = diff;
                }
            }
        }

        if (candidate is not null)
        {
            // An earlier candidate at the same distance wins (route scoped before global, inner causes first).
            candidates.TryAdd(candidateDiff, candidate);
        }

        return false;
    }

    private static int InheritanceLevel(Type type)
    {
        var level = 0;
        for (var t = type.BaseType; t is not null; t = t.BaseType)
        {
            level++;
        }

        return level;
    }
}
