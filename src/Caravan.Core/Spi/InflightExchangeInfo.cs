namespace Caravan.Spi;

/// <summary>A snapshot of an inflight exchange.</summary>
public sealed class InflightExchangeInfo
{
    /// <summary>Creates a snapshot.</summary>
    public InflightExchangeInfo(Exchange exchange, string routeId, TimeSpan duration)
    {
        Exchange = exchange;
        RouteId = routeId;
        Duration = duration;
    }

    /// <summary>The exchange.</summary>
    public Exchange Exchange { get; }

    /// <summary>The exchange id.</summary>
    public string ExchangeId => Exchange.ExchangeId;

    /// <summary>The route currently processing the exchange (the innermost one for nested <c>direct</c> calls).</summary>
    public string RouteId { get; }

    /// <summary>The route that created the exchange.</summary>
    public string? FromRouteId => Exchange.FromRouteId;

    /// <summary>How long the exchange has existed (since <see cref="Caravan.Exchange.Created"/>).</summary>
    public TimeSpan Duration { get; }

    /// <inheritdoc />
    public override string ToString() => $"Inflight[{ExchangeId} in {RouteId} for {Duration}]";
}
