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

    /// <summary>The variable whose value is sent as the body (Camel's <c>variableSend</c>); null sends the message body.</summary>
    public string? VariableSend { get; set; }

    /// <summary>
    /// The variable receiving the reply (Camel's <c>variableReceive</c>: body as the variable, headers as <c>name.header.key</c>); the message
    /// is then left unchanged.
    /// </summary>
    public string? VariableReceive { get; set; }

    /// <inheritdoc />
    public override string ShortName => "toD";

    /// <inheritdoc />
    public override string Label => $"toD[{Uri}]";

    /// <inheritdoc />
    public override IProcessor CreateProcessor(RouteContext routeContext) =>
        new SendDynamicProcessor(Uri, Pattern, CacheSize, IgnoreInvalidEndpoint) { VariableSend = VariableSend, VariableReceive = VariableReceive };
}
