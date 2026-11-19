using Caravan.Processors;
using Caravan.Spi;

namespace Caravan.Model;

/// <summary>Sets the body (<c>setBody</c>).</summary>
public sealed class SetBodyDefinition(IExpression expression) : ProcessorDefinition
{
    /// <summary>Computes the new body.</summary>
    public IExpression Expression { get; set; } = expression;

    /// <inheritdoc />
    public override string ShortName => "setBody";

    /// <inheritdoc />
    public override string Label => $"setBody[{Expression}]";

    /// <inheritdoc />
    public override IProcessor CreateProcessor(RouteContext routeContext) => new SetBodyProcessor(Expression);
}
