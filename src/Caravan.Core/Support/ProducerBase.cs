using Caravan.Spi;

namespace Caravan.Support;

/// <summary>Base class for producers.</summary>
public abstract class ProducerBase(IEndpoint endpoint) : ServiceSupport, IProducer
{
    /// <inheritdoc />
    public IEndpoint Endpoint { get; } = endpoint;

    /// <inheritdoc />
    public abstract ValueTask ProcessAsync(Exchange exchange);

    /// <inheritdoc />
    public override string ToString() => $"Producer[{Endpoint.EndpointUri}]";
}
