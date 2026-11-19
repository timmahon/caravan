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

    /// <summary>The variable whose value is sent as the body (Camel's <c>variableSend</c>); null sends the message body.</summary>
    public string? VariableSend { get; set; }

    /// <summary>
    /// The variable receiving the reply (Camel's <c>variableReceive</c>: body as the variable, headers as <c>name.header.key</c>); the message
    /// is then left unchanged.
    /// </summary>
    public string? VariableReceive { get; set; }

    /// <inheritdoc />
    public override string ShortName => "to";

    /// <inheritdoc />
    public override string Label => $"to[{Uri}]";

    /// <inheritdoc />
    public override IProcessor CreateProcessor(RouteContext routeContext) =>
        new SendProcessor(routeContext.ResolveEndpoint(Uri), Pattern) { VariableSend = VariableSend, VariableReceive = VariableReceive };
}
