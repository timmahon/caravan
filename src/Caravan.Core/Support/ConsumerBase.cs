using Caravan.Spi;

namespace Caravan.Support;

/// <summary>Base class for consumers.</summary>
public abstract class ConsumerBase(IEndpoint endpoint, IProcessor processor) : ServiceSupport, IConsumer
{
    /// <inheritdoc />
    public IEndpoint Endpoint { get; } = endpoint;

    /// <inheritdoc />
    public IProcessor Processor { get; } = processor;

    /// <summary>Creates an exchange originating from this consumer's endpoint.</summary>
    protected Exchange CreateExchange(ExchangePattern? pattern = null) => Endpoint.CreateExchange(pattern);

    /// <inheritdoc />
    public override string ToString() => $"Consumer[{Endpoint.EndpointUri}]";
}
