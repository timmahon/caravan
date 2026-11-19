using Caravan.Processors;
using Caravan.Spi;

namespace Caravan.Model;

/// <summary>
/// The <c>doTry</c> EIP: <see cref="OutputDefinition.Outputs"/> is the try block, followed by <c>doCatch</c> clauses
/// (<see cref="Catches"/>, the first match wins) and an optional <c>doFinally</c>.
/// </summary>
/// <remarks>
/// As in Camel, <c>doTry</c> is its own error handler: the route's error handler (redelivery, <c>onException</c>) is not applied to
/// the <c>doTry</c> node nor to any node inside its blocks. An exception no clause catches fails the exchange and is not handled by
/// <c>onException</c> either. Routes called from the try block (e.g. through <c>direct</c>) keep their own error handler.
/// </remarks>
public sealed class TryDefinition : OutputDefinition
{
    /// <summary>The <c>doCatch</c> clauses, in order.</summary>
    public List<CatchDefinition> Catches { get; } = [];

    /// <summary>The <c>doFinally</c> clause, if any.</summary>
    public FinallyDefinition? Finally { get; set; }

    /// <inheritdoc />
    public override string ShortName => "doTry";

    /// <inheritdoc />
    public override IEnumerable<ProcessorDefinition> Children
    {
        get
        {
            IEnumerable<ProcessorDefinition> children = [.. Outputs, .. Catches];
            return Finally is null ? children : children.Append(Finally);
        }
    }

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">The doTry has neither a doCatch nor a doFinally clause.</exception>
    public override IProcessor CreateProcessor(RouteContext routeContext)
    {
        if (Catches.Count == 0 && Finally is null)
        {
            throw new InvalidOperationException("doTry must have at least one doCatch or a doFinally clause");
        }

        var tryBlock = routeContext.CreateBlockProcessor(Outputs, this);
        var catches = new List<CatchProcessor>();
        foreach (var clause in Catches.Where(c => !c.Disabled))
        {
            Prepare(routeContext, clause);
            catches.Add(new CatchProcessor(clause.ExceptionTypes, clause.OnWhen, clause.CreateProcessor(routeContext)));
        }

        IProcessor? finallyBlock = null;
        if (Finally is { Disabled: false } f)
        {
            Prepare(routeContext, f);
            finallyBlock = f.CreateProcessor(routeContext);
        }

        return new TryProcessor(tryBlock, catches, finallyBlock);
    }

    private void Prepare(RouteContext routeContext, ProcessorDefinition clause)
    {
        clause.Parent = this;
        clause.Route ??= routeContext.Definition;
        clause.Id ??= routeContext.Context.NextNodeId(clause.ShortName);
    }
}
