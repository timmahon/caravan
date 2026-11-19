using Caravan.Support;

namespace Caravan.Components.Kamelet;

/// <summary>The producer of <c>kamelet:sink</c>: leaves the exchange unchanged.</summary>
internal sealed class KameletSinkProducer(KameletSinkEndpoint endpoint) : ProducerBase(endpoint)
{
    public override ValueTask ProcessAsync(Exchange exchange) => ValueTask.CompletedTask;
}
