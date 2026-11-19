using Caravan.Spi;

namespace Caravan.Model;

/// <summary>The <c>otherwise</c> clause of a <see cref="ChoiceDefinition"/>: runs when no <c>when</c> matched.</summary>
public sealed class OtherwiseDefinition : OutputDefinition
{
    /// <inheritdoc />
    public override string ShortName => "otherwise";

    /// <inheritdoc />
    public override IProcessor CreateProcessor(RouteContext routeContext) => routeContext.CreateBlockProcessor(Outputs, this);
}
