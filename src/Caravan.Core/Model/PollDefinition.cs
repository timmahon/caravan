using Caravan.Processors;
using Caravan.Spi;

namespace Caravan.Model;

/// <summary>
/// The <c>poll</c> EIP (Camel 4): receives a message from an endpoint with a polling consumer and makes it the exchange's message (body and
/// headers); when nothing arrives within <see cref="Timeout"/> the body becomes null. A simpler <c>pollEnrich</c> (no aggregation strategy).
/// </summary>
public sealed class PollDefinition(IExpression uri) : ProcessorDefinition
{
    /// <summary>Computes the endpoint URI (a Simple expression in the DSLs).</summary>
    public IExpression Uri { get; set; } = uri;

    /// <summary>How long to wait for a message (default 20 s, as Camel); negative waits forever, zero does not wait.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(20);

    /// <summary>The variable receiving the message instead of the exchange's message (Camel's <c>variableReceive</c>).</summary>
    public string? VariableReceive { get; set; }

    /// <inheritdoc />
    public override string ShortName => "poll";

    /// <inheritdoc />
    public override string Label => $"poll[{Uri}]";

    /// <inheritdoc />
    public override IProcessor CreateProcessor(RouteContext routeContext) =>
        new PollEnrichProcessor(Uri, Timeout < TimeSpan.Zero ? null : Timeout) { VariableReceive = VariableReceive };
}
