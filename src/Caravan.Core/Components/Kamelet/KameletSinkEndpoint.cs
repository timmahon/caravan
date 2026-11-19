using Caravan.Spi;
using Caravan.Support;

namespace Caravan.Components.Kamelet;

/// <summary>
/// The <c>kamelet:sink</c> endpoint: marks where a Kamelet's route hands the exchange back. For a Kamelet called with <c>to</c> sending to it does
/// nothing (the exchange returns to the caller when the route ends); for a Kamelet consumed with <c>from</c> (<see cref="RouteId"/> set) it
/// passes the exchange to the consuming route.
/// </summary>
public sealed class KameletSinkEndpoint : EndpointBase
{
    internal KameletSinkEndpoint(string uri, KameletComponent component)
        : base(uri, component, component.Context!)
    {
    }

    /// <summary>The id of the Kamelet route whose consumer receives the exchanges (set for Kamelets consumed with <c>from</c>).</summary>
    public string? RouteId { get; init; }

    internal KameletComponent KameletComponent => (KameletComponent)Component!;

    /// <inheritdoc />
    public override IProducer CreateProducer() => new KameletSinkProducer(this);

    /// <inheritdoc />
    public override IConsumer CreateConsumer(IProcessor processor) =>
        throw new NotSupportedException("Consuming from kamelet:sink is not supported");
}
