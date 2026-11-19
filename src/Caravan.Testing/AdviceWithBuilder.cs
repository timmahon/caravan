using Caravan.Builder;
using Caravan.Model;

namespace Caravan.Testing;

/// <summary>
/// Selects nodes of an advised route and changes them (Camel's <c>AdviceWithBuilder</c>), e.g.
/// <c>a.WeaveById("charge").Replace(r =&gt; r.To("mock:charge"))</c> or <c>a.WeaveByToUri("seda:*").SelectFirst().Remove()</c>.
/// By default every matching node is selected, at any depth (inside <c>choice</c>, <c>split</c>, ...); narrow the selection with
/// <see cref="SelectFirst"/>, <see cref="SelectLast"/>, <see cref="SelectIndex"/>, <see cref="SelectRange"/> and <see cref="MaxDeep"/>.
/// Changes are applied in order once the advice lambda returns; a weave that matches nothing fails the advice.
/// </summary>
public sealed class AdviceWithBuilder
{
    private readonly AdviceWithRouteBuilder _owner;
    private readonly Func<ProcessorDefinition, bool> _matches;
    private readonly string _description;
    private int _from;
    private int _to = int.MaxValue;
    private bool _last;
    private int _maxDeep = int.MaxValue;

    internal AdviceWithBuilder(AdviceWithRouteBuilder owner, string description, Func<ProcessorDefinition, bool> matches)
    {
        _owner = owner;
        _description = description;
        _matches = matches;
    }

    /// <summary>Selects only the first matching node.</summary>
    /// <returns>This builder.</returns>
    public AdviceWithBuilder SelectFirst() => SelectRange(0, 0);

    /// <summary>Selects only the last matching node.</summary>
    /// <returns>This builder.</returns>
    public AdviceWithBuilder SelectLast()
    {
        _last = true;
        return this;
    }

    /// <summary>Selects only the matching node at <paramref name="index"/> (0-based, in route order).</summary>
    /// <param name="index">The index among the matching nodes.</param>
    /// <returns>This builder.</returns>
    public AdviceWithBuilder SelectIndex(int index) => SelectRange(index, index);

    /// <summary>Selects the matching nodes from index <paramref name="from"/> to <paramref name="to"/> (0-based, both included).</summary>
    /// <param name="from">The first index.</param>
    /// <param name="to">The last index.</param>
    /// <returns>This builder.</returns>
    public AdviceWithBuilder SelectRange(int from, int to)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(from);
        ArgumentOutOfRangeException.ThrowIfLessThan(to, from);
        _from = from;
        _to = to;
        _last = false;
        return this;
    }

    /// <summary>Only matches nodes up to <paramref name="level"/> levels deep (1 = the route's top-level steps only).</summary>
    /// <param name="level">The maximum depth.</param>
    /// <returns>This builder.</returns>
    public AdviceWithBuilder MaxDeep(int level)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(level, 1);
        _maxDeep = level;
        return this;
    }

    /// <summary>Replaces each selected node with the steps built by <paramref name="replacement"/> (none removes it).</summary>
    /// <param name="replacement">Builds the replacing steps.</param>
    public void Replace(Action<RouteSteps> replacement)
    {
        ArgumentNullException.ThrowIfNull(replacement);
        _owner.AddTask(route =>
        {
            foreach (var match in Select(route))
            {
                DefinitionTree.Replace(route, match, DefinitionTree.Build(route, match.Parent, replacement));
            }
        });
    }

    /// <summary>Removes each selected node.</summary>
    public void Remove() => _owner.AddTask(route =>
    {
        foreach (var match in Select(route))
        {
            DefinitionTree.Remove(route, match);
        }
    });

    /// <summary>Adds the steps built by <paramref name="steps"/> before each selected node.</summary>
    /// <param name="steps">Builds the steps.</param>
    public void Before(Action<RouteSteps> steps)
    {
        ArgumentNullException.ThrowIfNull(steps);
        _owner.AddTask(route =>
        {
            foreach (var match in Select(route))
            {
                DefinitionTree.InsertBefore(route, match, DefinitionTree.Build(route, match.Parent, steps));
            }
        });
    }

    /// <summary>Adds the steps built by <paramref name="steps"/> after each selected node.</summary>
    /// <param name="steps">Builds the steps.</param>
    public void After(Action<RouteSteps> steps)
    {
        ArgumentNullException.ThrowIfNull(steps);
        _owner.AddTask(route =>
        {
            foreach (var match in Select(route))
            {
                DefinitionTree.InsertAfter(route, match, DefinitionTree.Build(route, match.Parent, steps));
            }
        });
    }

    private List<WeaveMatch> Select(RouteDefinition route)
    {
        var matches = DefinitionTree.Walk(route).Where(m => m.Depth <= _maxDeep && _matches(m.Node)).ToList();
        List<WeaveMatch> selected = _last
            ? [.. matches.TakeLast(1)]
            : [.. matches.Where((_, i) => i >= _from && i <= _to)];
        if (selected.Count == 0)
        {
            throw new InvalidOperationException(
                $"There are no outputs which matches: {_description} in the route: {route.Id}" +
                (matches.Count > 0 ? $" (selection out of the {matches.Count} matching outputs)" : string.Empty));
        }

        return selected;
    }
}
