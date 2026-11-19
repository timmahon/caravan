using Caravan.Spi;
using Caravan.Support;

namespace Caravan.Components.Ref;

/// <summary>A <c>ref</c> endpoint delegating to a registry endpoint that is not an <see cref="EndpointBase"/>.</summary>
public sealed class RefEndpoint : EndpointBase
{
    internal RefEndpoint(string uri, RefComponent component, IEndpoint target)
        : base(uri, component, component.Context!)
    {
        Target = target;
    }

    /// <summary>The registry endpoint.</summary>
    public IEndpoint Target { get; }

    /// <inheritdoc />
    public override IProducer CreateProducer() => Target.CreateProducer();

    /// <inheritdoc />
    public override IConsumer CreateConsumer(IProcessor processor) => Target.CreateConsumer(processor);

    /// <inheritdoc />
    public override IPollingConsumer CreatePollingConsumer() =>
        Target is IPollingConsumerEndpoint polling ? polling.CreatePollingConsumer() : base.CreatePollingConsumer();

    /// <inheritdoc />
    protected override Task DoStartAsync(CancellationToken cancellationToken) => Target.StartAsync(cancellationToken);

    /// <inheritdoc />
    protected override Task DoStopAsync(CancellationToken cancellationToken) => Target.StopAsync(cancellationToken);
}
