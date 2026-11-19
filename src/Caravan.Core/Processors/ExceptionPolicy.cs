using Caravan.Model;
using Caravan.Spi;

namespace Caravan.Processors;

/// <summary>An <c>onException</c> clause prepared for one route: its merged redelivery policy and its steps.</summary>
internal sealed class ExceptionPolicy(OnExceptionDefinition definition, RedeliveryPolicy redeliveryPolicy, IProcessor? processor)
{
    public OnExceptionDefinition Definition { get; } = definition;

    public RedeliveryPolicy RedeliveryPolicy { get; } = redeliveryPolicy;

    /// <summary>The clause's steps, or null when it has none.</summary>
    public IProcessor? Processor { get; } = processor;

    public bool Matches(Exchange exchange) => Definition.OnWhen is not { } when || when.Matches(exchange);

    public override string ToString() => Definition.Label;
}
