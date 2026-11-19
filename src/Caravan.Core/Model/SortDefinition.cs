using Caravan.Processors;
using Caravan.Spi;

namespace Caravan.Model;

/// <summary>The <c>sort</c> EIP: sorts the items of the body (or of an expression) into a list body; see <see cref="SortProcessor"/>.</summary>
public sealed class SortDefinition : ProcessorDefinition
{
    /// <summary>Computes the items to sort; null for the body.</summary>
    public IExpression? Expression { get; set; }

    /// <summary>Orders the items; null for natural ordering.</summary>
    public IComparer<object?>? Comparer { get; set; }

    /// <inheritdoc />
    public override string ShortName => "sort";

    /// <inheritdoc />
    public override string Label => $"sort[{Expression?.ToString() ?? "${body}"}]";

    /// <inheritdoc />
    public override IProcessor CreateProcessor(RouteContext routeContext) => new SortProcessor(Expression ?? Expr.Body(), Comparer);
}
