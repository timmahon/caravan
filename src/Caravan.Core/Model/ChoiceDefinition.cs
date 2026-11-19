using Caravan.Processors;
using Caravan.Spi;

namespace Caravan.Model;

/// <summary>
/// The <c>choice</c> EIP (content-based router): the first matching <c>when</c> runs, otherwise the optional
/// <c>otherwise</c>; routing continues after the choice. Each evaluated <c>when</c> sets
/// <see cref="ExchangePropertyKeys.FilterMatched"/>, as in Camel.
/// </summary>
public sealed class ChoiceDefinition : ProcessorDefinition
{
    /// <summary>The <c>when</c> clauses, evaluated in order.</summary>
    public List<WhenDefinition> WhenClauses { get; } = [];

    /// <summary>The <c>otherwise</c> clause, if any.</summary>
    public OtherwiseDefinition? Otherwise { get; set; }

    /// <inheritdoc />
    public override string ShortName => "choice";

    /// <inheritdoc />
    public override IEnumerable<ProcessorDefinition> Children =>
        Otherwise is null ? WhenClauses : [.. WhenClauses, Otherwise];

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">The choice has no <c>when</c> clause.</exception>
    public override IProcessor CreateProcessor(RouteContext routeContext)
    {
        if (WhenClauses.Count == 0)
        {
            throw new InvalidOperationException("Choice has no when clauses configured");
        }

        var whens = new List<FilterProcessor>();
        foreach (var when in WhenClauses.Where(w => !w.Disabled))
        {
            Prepare(routeContext, when);
            whens.Add((FilterProcessor)when.CreateProcessor(routeContext));
        }

        IProcessor? otherwise = null;
        if (Otherwise is { Disabled: false } o)
        {
            Prepare(routeContext, o);
            otherwise = o.CreateProcessor(routeContext);
        }

        return new ChoiceProcessor(whens, otherwise);
    }

    private void Prepare(RouteContext routeContext, ProcessorDefinition clause)
    {
        clause.Parent = this;
        clause.Route ??= routeContext.Definition;
        clause.Id ??= routeContext.Context.NextNodeId(clause.ShortName);
    }
}
