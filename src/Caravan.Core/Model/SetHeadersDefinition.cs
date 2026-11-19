using Caravan.Processors;
using Caravan.Spi;

namespace Caravan.Model;

/// <summary>The <c>setHeaders</c> EIP: sets several headers at once, in order; see <see cref="SetHeadersProcessor"/>.</summary>
public sealed class SetHeadersDefinition : ProcessorDefinition
{
    /// <summary>The headers and their expressions, in order.</summary>
    public List<KeyValuePair<string, IExpression>> Headers { get; } = [];

    /// <inheritdoc />
    public override string ShortName => "setHeaders";

    /// <inheritdoc />
    public override string Label => $"setHeaders[{string.Join(", ", Headers.Select(h => h.Key))}]";

    /// <inheritdoc />
    public override IProcessor CreateProcessor(RouteContext routeContext) => new SetHeadersProcessor([.. Headers]);
}
