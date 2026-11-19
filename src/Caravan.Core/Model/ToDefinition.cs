using Caravan.Processors;
using Caravan.Spi;

namespace Caravan.Model;

/// <summary>Sends to a static endpoint URI (<c>to</c>).</summary>
public sealed class ToDefinition(string uri, ExchangePattern? pattern = null) : ProcessorDefinition
{
    /// <summary>The endpoint URI (placeholders are resolved).</summary>
    public string Uri { get; set; } = uri;

    /// <summary>The exchange pattern to send with; null keeps the exchange's pattern.</summary>
    public ExchangePattern? Pattern { get; set; } = pattern;

    /// <inheritdoc />
    public override string ShortName => "to";

    /// <inheritdoc />
    public override string Label => $"to[{Uri}]";

    /// <inheritdoc />
    public override IProcessor CreateProcessor(RouteContext routeContext) =>
        new SendProcessor(routeContext.ResolveEndpoint(Uri), Pattern);
}
