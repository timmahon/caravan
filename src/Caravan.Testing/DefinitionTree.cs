using System.Collections;
using System.Reflection;
using Caravan.Builder;
using Caravan.Model;

namespace Caravan.Testing;

/// <summary>Navigates and edits a route's definition tree for AdviceWith.</summary>
internal static class DefinitionTree
{
    /// <summary>All nodes of the route's steps, depth-first in route order, with parents and depths.</summary>
    public static List<WeaveMatch> Walk(RouteDefinition route)
    {
        var result = new List<WeaveMatch>();
        foreach (var node in route.Outputs)
        {
            Walk(node, null, 1, result);
        }

        return result;
    }

    /// <summary>Builds new step definitions to put under <paramref name="parent"/> (null: the route's top level).</summary>
    public static List<ProcessorDefinition> Build(RouteDefinition route, ProcessorDefinition? parent, Action<RouteSteps> configure)
    {
        var outputs = new List<ProcessorDefinition>();
        configure(new RouteSteps(outputs, route, parent));
        return outputs;
    }

    public static void Replace(RouteDefinition route, WeaveMatch match, IReadOnlyList<ProcessorDefinition> replacement)
    {
        if (FindList(route, match) is { } list)
        {
            var index = IndexOf(list, match.Node);
            list.RemoveAt(index);
            Insert(list, index, replacement, match);
            return;
        }

        var property = FindProperty(match);
        if (replacement.Count > 1 || (replacement.Count == 1 && !property.PropertyType.IsInstanceOfType(replacement[0])))
        {
            throw new InvalidOperationException(
                $"Cannot replace {match.Node} with {string.Join(", ", replacement)}: {match.Parent} holds a single {property.PropertyType.Name}");
        }

        property.SetValue(match.Parent, replacement.Count == 0 ? null : replacement[0]);
    }

    public static void Remove(RouteDefinition route, WeaveMatch match)
    {
        if (FindList(route, match) is { } list)
        {
            list.RemoveAt(IndexOf(list, match.Node));
            return;
        }

        FindProperty(match).SetValue(match.Parent, null);
    }

    public static void InsertBefore(RouteDefinition route, WeaveMatch match, IReadOnlyList<ProcessorDefinition> nodes) =>
        InsertAt(route, match, nodes, after: false);

    public static void InsertAfter(RouteDefinition route, WeaveMatch match, IReadOnlyList<ProcessorDefinition> nodes) =>
        InsertAt(route, match, nodes, after: true);

    private static void InsertAt(RouteDefinition route, WeaveMatch match, IReadOnlyList<ProcessorDefinition> nodes, bool after)
    {
        var list = FindList(route, match) ?? throw new InvalidOperationException(
            $"Cannot add steps {(after ? "after" : "before")} {match.Node}: it is not in a list of steps of {match.Parent}");
        var index = IndexOf(list, match.Node);
        Insert(list, after ? index + 1 : index, nodes, match);
    }

    private static void Insert(IList list, int index, IReadOnlyList<ProcessorDefinition> nodes, WeaveMatch match)
    {
        foreach (var node in nodes)
        {
            try
            {
                list.Insert(index++, node);
            }
            catch (ArgumentException e)
            {
                throw new InvalidOperationException($"Cannot put {node} where {match.Node} is: {match.Parent} only accepts other kinds of nodes there", e);
            }
        }
    }

    private static void Walk(ProcessorDefinition node, ProcessorDefinition? parent, int depth, List<WeaveMatch> result)
    {
        result.Add(new WeaveMatch(node, parent, depth));
        foreach (var child in node.Children.ToList())
        {
            Walk(child, node, depth + 1, result);
        }
    }

    private static int IndexOf(IList list, ProcessorDefinition node)
    {
        for (var i = 0; i < list.Count; i++)
        {
            if (ReferenceEquals(list[i], node))
            {
                return i;
            }
        }

        throw new InvalidOperationException($"{node} is no longer in the route");
    }

    /// <summary>The list holding the node: the route's steps at the top level, else a list property of the parent containing it.</summary>
    private static IList? FindList(RouteDefinition route, WeaveMatch match)
    {
        if (match.Parent is null)
        {
            return route.Outputs;
        }

        foreach (var property in Properties(match.Parent))
        {
            if (typeof(IList).IsAssignableFrom(property.PropertyType) && property.GetValue(match.Parent) is IList list &&
                list.Cast<object?>().Any(item => ReferenceEquals(item, match.Node)))
            {
                return list;
            }
        }

        return null;
    }

    /// <summary>The single-valued property of the parent holding the node (e.g. a choice's <c>otherwise</c>).</summary>
    private static PropertyInfo FindProperty(WeaveMatch match) =>
        Properties(match.Parent!).FirstOrDefault(p => p.CanWrite && ReferenceEquals(p.GetValue(match.Parent), match.Node))
        ?? throw new InvalidOperationException($"Cannot find where {match.Parent} holds {match.Node}");

    private static IEnumerable<PropertyInfo> Properties(ProcessorDefinition parent) =>
        parent.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.GetIndexParameters().Length == 0 && p.Name is not nameof(ProcessorDefinition.Parent) and not nameof(ProcessorDefinition.Children));
}
