using Caravan.Spi;
using Caravan.Support;

namespace Caravan.Testing.Notify;

/// <summary>Only exchanges created by a route whose id matches the pattern (Camel's <c>fromRoute</c>).</summary>
internal sealed class FromRouteNotifyFilter(string pattern) : ExchangeIdNotifyFilter
{
    public override string ToString() => $"fromRoute({pattern})";

    protected override bool Accepts(Exchange exchange, CaravanEvent evt) =>
        (exchange.FromRouteId ?? evt.RouteId) is { } routeId && PatternHelper.Matches(routeId, pattern);
}
