using Caravan.Spi;
using Caravan.Support;

namespace Caravan.Components.Kamelet;

/// <summary>The <c>kamelet:source?routeId=x</c> endpoint: the input of a route created from a Kamelet template.</summary>
public sealed class KameletSourceEndpoint : EndpointBase
{
    internal KameletSourceEndpoint(string uri, KameletComponent component, string routeId)
        : base(uri, component, component.Context!)
    {
        RouteId = routeId;
    }

    /// <summary>The id of the route consuming from the endpoint.</summary>
    public string RouteId { get; }

    internal KameletComponent KameletComponent => (KameletComponent)Component!;

    /// <inheritdoc />
    public override IProducer CreateProducer() =>
        throw new NotSupportedException($"Cannot send to {EndpointUri}: call the kamelet with kamelet:templateId instead");

    /// <inheritdoc />
    public override IConsumer CreateConsumer(IProcessor processor) => new KameletSourceConsumer(this, processor);
}
