using Caravan.Processors;
using Caravan.Support;

namespace Caravan.Components.Bean;

/// <summary>Calls the bean method of a <c>bean</c> or <c>class</c> endpoint.</summary>
internal sealed class BeanProducer(BeanEndpoint endpoint, BeanProcessor processor) : ProducerBase(endpoint)
{
    public override ValueTask ProcessAsync(Exchange exchange) => processor.ProcessAsync(exchange);
}
