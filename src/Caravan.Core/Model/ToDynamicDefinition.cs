using Caravan.Processors;
using Caravan.Spi;

namespace Caravan.Model;

/// <summary>Sends to an endpoint whose URI is computed per exchange (<c>toD</c>); see <see cref="SendDynamicProcessor"/>.</summary>
public sealed class ToDynamicDefinition(IExpression uri) : ProcessorDefinition
{
    /// <summary>Computes the endpoint URI (or endpoint).</summary>
    public IExpression Uri { get; set; } = uri;

    /// <summary>The exchange pattern to send with; null keeps the exchange's pattern.</summary>
    public ExchangePattern? Pattern { get; set; }

    /// <summary>Maximum cached producers (default 1000; negative disables caching).</summary>
    public int CacheSize { get; set; } = SendDynamicProcessor.DefaultCacheSize;

    /// <summary>Whether an unresolvable URI is skipped instead of failing the exchange.</summary>
    public bool IgnoreInvalidEndpoint { get; set; }

    /// <inheritdoc />
    public override string ShortName => "toD";

    /// <inheritdoc />
    public override string Label => $"toD[{Uri}]";

    /// <inheritdoc />
    public override IProcessor CreateProcessor(RouteContext routeContext) =>
        new SendDynamicProcessor(Uri, Pattern, CacheSize, IgnoreInvalidEndpoint);
}
