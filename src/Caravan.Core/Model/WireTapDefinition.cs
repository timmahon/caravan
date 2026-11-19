using Caravan.Processors;
using Caravan.Spi;

namespace Caravan.Model;

/// <summary>Sends a copy of the exchange to an endpoint asynchronously (<c>wireTap</c>); see <see cref="WireTapProcessor"/>.</summary>
public sealed class WireTapDefinition(IExpression uri) : ProcessorDefinition
{
    /// <summary>Computes the endpoint URI, evaluated on the tapped exchange.</summary>
    public IExpression Uri { get; set; } = uri;

    /// <summary>True (default) to tap a copy of the exchange; false to tap a new, empty exchange.</summary>
    public bool Copy { get; set; } = true;

    /// <summary>Called on the tapped exchange before it is sent.</summary>
    public Action<Exchange>? OnPrepare { get; set; }

    /// <summary>Maximum cached producers for dynamic URIs (default 1000; negative disables caching).</summary>
    public int CacheSize { get; set; } = SendDynamicProcessor.DefaultCacheSize;

    /// <summary>Whether an unresolvable URI is skipped instead of logged as a failed tap.</summary>
    public bool IgnoreInvalidEndpoint { get; set; }

    /// <summary>The variable whose value the tap sends as the body (Camel's <c>variableSend</c>); null sends the message body.</summary>
    public string? VariableSend { get; set; }

    /// <inheritdoc />
    public override string ShortName => "wireTap";

    /// <inheritdoc />
    public override string Label => $"wireTap[{Uri}]";

    /// <inheritdoc />
    public override IProcessor CreateProcessor(RouteContext routeContext) =>
        new WireTapProcessor(new SendDynamicProcessor(Uri, ExchangePattern.InOnly, CacheSize, IgnoreInvalidEndpoint) { VariableSend = VariableSend }, Copy, OnPrepare);
}
