using Caravan.Processors;
using Caravan.Spi;

namespace Caravan.Model;

/// <summary>
/// The <c>loop</c> EIP: runs the nested steps a number of times (<see cref="Count"/>) or while a predicate matches (<see cref="While"/>,
/// Camel's <c>loopDoWhile</c>); see <see cref="LoopProcessor"/>.
/// </summary>
public sealed class LoopDefinition : OutputDefinition
{
    /// <summary>Computes the number of iterations (evaluated once); null for a while loop.</summary>
    public IExpression? Count { get; set; }

    /// <summary>The condition checked before each iteration; null for a counted loop.</summary>
    public IPredicate? While { get; set; }

    /// <summary>Whether each iteration starts from a copy of the exchange as it entered the loop (else each processes the previous output).</summary>
    public bool Copy { get; set; }

    /// <summary>Whether the loop ends quietly when the exchange is cancelled or the context stops.</summary>
    public bool BreakOnShutdown { get; set; }

    /// <inheritdoc />
    public override string ShortName => "loop";

    /// <inheritdoc />
    public override string Label => Count is not null ? $"loop[{Count}]" : $"loop[while {While}]";

    /// <inheritdoc />
    public override IProcessor CreateProcessor(RouteContext routeContext) =>
        new LoopProcessor(routeContext.CreateBlockProcessor(Outputs, this), Count, While, Copy, BreakOnShutdown);
}
