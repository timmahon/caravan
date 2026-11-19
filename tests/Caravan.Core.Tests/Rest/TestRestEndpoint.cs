using Caravan.Spi;
using Caravan.Support;

namespace Caravan.Core.Tests.Rest;

public sealed class TestRestEndpoint(string uri, TestRestComponent component) : EndpointBase(uri, component, component.Context!)
{
    public override IProducer CreateProducer() => throw new NotSupportedException();

    public override IConsumer CreateConsumer(IProcessor processor) => throw new NotSupportedException();
}
