using Caravan.Processors;
using Caravan.Spi;

namespace Caravan.Model;

/// <summary>A <c>when</c> clause of a <see cref="ChoiceDefinition"/>: nested steps run when <see cref="Predicate"/> matches.</summary>
public sealed class WhenDefinition(IPredicate predicate) : OutputDefinition
{
    /// <summary>The condition.</summary>
    public IPredicate Predicate { get; set; } = predicate;

    /// <inheritdoc />
    public override string ShortName => "when";

    /// <inheritdoc />
    public override string Label => $"when[{Predicate}]";

    /// <summary>Creates a <see cref="FilterProcessor"/> for this clause.</summary>
    public override IProcessor CreateProcessor(RouteContext routeContext) =>
        new FilterProcessor(Predicate, routeContext.CreateBlockProcessor(Outputs, this));
}
