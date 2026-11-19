using Caravan.Processors;
using Caravan.Spi;

namespace Caravan.Model;

/// <summary>The <c>script</c> EIP: evaluates an expression for its side effects, ignoring the result; see <see cref="ScriptProcessor"/>.</summary>
public sealed class ScriptDefinition(IExpression expression) : ProcessorDefinition
{
    /// <summary>The expression to evaluate.</summary>
    public IExpression Expression { get; set; } = expression;

    /// <inheritdoc />
    public override string ShortName => "script";

    /// <inheritdoc />
    public override string Label => $"script[{Expression}]";

    /// <inheritdoc />
    public override IProcessor CreateProcessor(RouteContext routeContext) => new ScriptProcessor(Expression);
}
