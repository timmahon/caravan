using Caravan.Spi;
using Caravan.Support;

namespace Caravan.Testing.Notify;

/// <summary>Only exchanges that entered through an endpoint matching the pattern (Camel's <c>from</c>).</summary>
internal sealed class FromEndpointNotifyFilter(ICaravanContext context, string pattern) : ExchangeIdNotifyFilter
{
    public override string ToString() => $"from({pattern})";

    protected override bool Accepts(Exchange exchange, CaravanEvent evt) =>
        (exchange.FromEndpoint ?? evt.Endpoint)?.EndpointUri is { } uri && EndpointHelper.MatchEndpoint(context, uri, pattern);
}
