using Caravan.Spi;
using Caravan.Support;

namespace Caravan.Components.Kamelet;

/// <summary>
/// The <c>kamelet:sink</c> endpoint: marks where a Kamelet's route hands the exchange back to its caller. Kamelet calls are synchronous, so
/// sending to it does nothing: the exchange returns to the caller when the route ends.
/// </summary>
public sealed class KameletSinkEndpoint : EndpointBase
{
    internal KameletSinkEndpoint(string uri, KameletComponent component)
        : base(uri, component, component.Context!)
    {
    }

    /// <inheritdoc />
    public override IProducer CreateProducer() => new KameletSinkProducer(this);

    /// <inheritdoc />
    public override IConsumer CreateConsumer(IProcessor processor) =>
        throw new NotSupportedException("Consuming from kamelet:sink is not supported");
}
