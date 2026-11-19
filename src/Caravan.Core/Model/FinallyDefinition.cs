using Caravan.Spi;

namespace Caravan.Model;

/// <summary>The <c>doFinally</c> clause of a <c>doTry</c>: steps that always run after the try block and any catch clause.</summary>
public sealed class FinallyDefinition : OutputDefinition
{
    /// <inheritdoc />
    public override string ShortName => "doFinally";

    /// <inheritdoc />
    public override IProcessor CreateProcessor(RouteContext routeContext) => routeContext.CreateBlockProcessor(Outputs, this);
}
