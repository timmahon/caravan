using System.Collections.Concurrent;
using System.Reflection;
using Caravan.Model;

namespace Caravan.Language;

/// <summary>
/// Compiles, when a route is created, the predicate form of the <see cref="SimpleBuilder"/>s its nodes use as predicates (like
/// Camel's <c>Predicate.init</c>), so a malformed Simple predicate fails the route's creation rather than its first exchange.
/// </summary>
internal static class SimpleRouteValidator
{
    private static readonly ConcurrentDictionary<Type, PropertyInfo[]> PredicateProperties = new();

    /// <summary>Validates the nodes of <paramref name="route"/> (steps, onException, onCompletion and intercept blocks).</summary>
    public static void Validate(RouteDefinition route)
    {
        var roots = route.Outputs
            .Concat(route.OnExceptions)
            .Concat(route.OnCompletions)
            .Concat(route.Intercepts)
            .Concat(route.InterceptFroms)
            .Concat(route.InterceptSendToEndpoints);
        var stack = new Stack<ProcessorDefinition>(roots);
        var seen = new HashSet<ProcessorDefinition>(ReferenceEqualityComparer.Instance);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            if (!seen.Add(node))
            {
                continue;
            }

            foreach (var property in PredicateProperties.GetOrAdd(node.GetType(), FindPredicateProperties))
            {
                if (property.GetValue(node) is SimpleBuilder builder)
                {
                    builder.AsPredicate();
                }
            }

            foreach (var child in node.Children)
            {
                stack.Push(child);
            }
        }
    }

    private static PropertyInfo[] FindPredicateProperties(Type type) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.GetIndexParameters().Length == 0 && typeof(IPredicate).IsAssignableFrom(p.PropertyType))
            .ToArray();
}
