using Caravan.Processors;
using Caravan.Spi;

namespace Caravan.Model;

/// <summary>
/// The <c>filter</c> EIP: the nested steps run only when <see cref="Predicate"/> matches; routing continues after
/// the filter for every exchange. Sets <see cref="ExchangePropertyKeys.FilterMatched"/>.
/// </summary>
public sealed class FilterDefinition(IPredicate predicate) : OutputDefinition
{
    /// <summary>The condition.</summary>
    public IPredicate Predicate { get; set; } = predicate;

    /// <inheritdoc />
    public override string ShortName => "filter";

    /// <inheritdoc />
    public override string Label => $"filter[{Predicate}]";

    /// <inheritdoc />
    public override IProcessor CreateProcessor(RouteContext routeContext) =>
        new FilterProcessor(Predicate, routeContext.CreateBlockProcessor(Outputs, this));
}
