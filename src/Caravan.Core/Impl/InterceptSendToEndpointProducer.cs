using Caravan.Spi;
using Caravan.Support;

namespace Caravan.Impl;

/// <summary>
/// Wraps an endpoint's producer and runs the context's <see cref="ISendToEndpointInterceptor"/>s matching the endpoint around each send
/// (Camel's <c>InterceptSendToEndpoint</c> endpoint decorator, applied to producers so that it covers every sender).
/// </summary>
internal sealed class InterceptSendToEndpointProducer(IProducer producer, SendToEndpointInterceptors interceptors) : ServiceSupport, IProducer
{
    private volatile Chain? _chain;

    public IEndpoint Endpoint => producer.Endpoint;

    /// <summary>The wrapped producer.</summary>
    public IProducer Producer => producer;

    public ValueTask ProcessAsync(Exchange exchange)
    {
        var (current, version) = interceptors.Snapshot();
        if (current.Length == 0)
        {
            return producer.ProcessAsync(exchange);
        }

        var chain = _chain;
        if (chain is null || chain.Version != version)
        {
            chain = _chain = BuildChain(current, version);
        }

        return chain.Processor.ProcessAsync(exchange);
    }

    public override string ToString() => producer.ToString() ?? base.ToString()!;

    protected override Task DoStartAsync(CancellationToken cancellationToken) => producer.StartAsync(cancellationToken);

    protected override Task DoStopAsync(CancellationToken cancellationToken) => producer.StopAsync(cancellationToken);

    private Chain BuildChain(ISendToEndpointInterceptor[] current, int version)
    {
        IProcessor next = producer;
        foreach (var interceptor in current.Where(i => i.Matches(producer.Endpoint)).Reverse())
        {
            next = new Link(interceptor, producer.Endpoint, next);
        }

        return new Chain(version, next);
    }

    private sealed record Chain(int Version, IProcessor Processor);

    private sealed class Link(ISendToEndpointInterceptor interceptor, IEndpoint endpoint, IProcessor next) : IProcessor
    {
        public ValueTask ProcessAsync(Exchange exchange) => interceptor.SendAsync(exchange, endpoint, next);
    }
}
