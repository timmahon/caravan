using Caravan.Processors;
using Caravan.Spi;

namespace Caravan.Model;

/// <summary>Transforms the body (<c>transform</c>). Same runtime behaviour as <see cref="SetBodyDefinition"/>.</summary>
public sealed class TransformDefinition(IExpression expression) : ProcessorDefinition
{
    /// <summary>Computes the new body.</summary>
    public IExpression Expression { get; set; } = expression;

    /// <inheritdoc />
    public override string ShortName => "transform";

    /// <inheritdoc />
    public override string Label => $"transform[{Expression}]";

    /// <inheritdoc />
    public override IProcessor CreateProcessor(RouteContext routeContext) => new SetBodyProcessor(Expression);
}
