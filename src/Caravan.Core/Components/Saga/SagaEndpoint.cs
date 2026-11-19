using Caravan.Spi;
using Caravan.Support;

namespace Caravan.Components.Saga;

/// <summary>A <c>saga</c> endpoint (<c>saga:complete</c> or <c>saga:compensate</c>); producer only.</summary>
public sealed class SagaEndpoint(string uri, SagaComponent component, SagaEndpointAction action)
    : EndpointBase(uri, component, component.Context!)
{
    /// <summary>What the endpoint does with the exchange's saga.</summary>
    public SagaEndpointAction Action { get; } = action;

    /// <inheritdoc />
    public override IProducer CreateProducer() => new SagaProducer(this);

    /// <summary>Not supported: the saga component only has producers.</summary>
    /// <param name="processor">Ignored.</param>
    /// <returns>Never returns.</returns>
    /// <exception cref="NotSupportedException">Always.</exception>
    public override IConsumer CreateConsumer(IProcessor processor) =>
        throw new NotSupportedException("The saga component does not support consumers");
}
