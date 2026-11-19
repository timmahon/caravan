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

    ICaravanContext Context { get; }

    IComponent? Component { get; }

    /// <summary>Whether one instance of this endpoint is shared for its URI.</summary>
    bool IsSingleton { get; }

    /// <summary>Whether unknown URI parameters are tolerated.</summary>
    bool IsLenientProperties { get; }

    IProducer CreateProducer();

    IConsumer CreateConsumer(IProcessor processor);

    Exchange CreateExchange(ExchangePattern? pattern = null);
}

/// <summary>Sends exchanges to an endpoint.</summary>
public interface IProducer : IProcessor, IService
{
    IEndpoint Endpoint { get; }
}

/// <summary>Receives messages from an endpoint and feeds them to a processor.</summary>
public interface IConsumer : IService
{
    IEndpoint Endpoint { get; }

    IProcessor Processor { get; }
}

/// <summary>A factory of endpoints for a URI scheme (e.g. <c>direct</c>, <c>seda</c>).</summary>
public interface IComponent : IService
{
    ICaravanContext? Context { get; set; }

    /// <summary>Creates an endpoint for the full <paramref name="uri"/>.</summary>
    IEndpoint CreateEndpoint(string uri);
}
