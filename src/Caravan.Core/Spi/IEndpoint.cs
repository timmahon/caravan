namespace Caravan.Spi;

/// <summary>
/// An endpoint identified by a URI such as <c>direct:start</c> or <c>seda:orders?concurrentConsumers=4</c>.
/// Endpoints create producers (to send messages) and consumers (to receive them).
/// </summary>
public interface IEndpoint : IService
{
    /// <summary>The endpoint URI as given (normalized).</summary>
    string EndpointUri { get; }

    /// <summary>The key used to cache this endpoint in the context.</summary>
    string EndpointKey { get; }

    /// <summary>The context the endpoint belongs to.</summary>
    ICaravanContext Context { get; }

    /// <summary>The component that created the endpoint, if any.</summary>
    IComponent? Component { get; }

    /// <summary>Whether one instance of this endpoint is shared for its URI.</summary>
    bool IsSingleton { get; }

    /// <summary>Whether unknown URI parameters are tolerated.</summary>
    bool IsLenientProperties { get; }

    /// <summary>Creates a producer that sends exchanges to this endpoint.</summary>
    IProducer CreateProducer();

    /// <summary>Creates a consumer that feeds exchanges from this endpoint to <paramref name="processor"/>.</summary>
    IConsumer CreateConsumer(IProcessor processor);

    /// <summary>Creates a new exchange from this endpoint.</summary>
    Exchange CreateExchange(ExchangePattern? pattern = null);
}
