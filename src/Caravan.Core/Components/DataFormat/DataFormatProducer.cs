using Caravan.Spi;
using Caravan.Support;

namespace Caravan.Components.DataFormat;

/// <summary>Runs the marshal or unmarshal processor of a <c>dataformat</c> endpoint.</summary>
internal sealed class DataFormatProducer(DataFormatEndpoint endpoint, IProcessor processor) : ProducerBase(endpoint)
{
    public override ValueTask ProcessAsync(Exchange exchange) => processor.ProcessAsync(exchange);
}
