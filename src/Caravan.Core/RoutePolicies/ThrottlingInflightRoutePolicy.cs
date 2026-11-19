using Caravan.Spi;
using Microsoft.Extensions.Logging;

namespace Caravan.RoutePolicies;

/// <summary>
/// Throttles a route by its number of inflight exchanges (Camel's <c>ThrottlingInflightRoutePolicy</c>): when an exchange is done and more than
/// <see cref="MaxInflightExchanges"/> exchanges are still inflight (in the route, or in the context with <see cref="ThrottlingScope.Context"/>),
/// the route's consumer is suspended; it is resumed once the count drops to <see cref="ResumePercentOfMax"/> percent of the maximum. Useful with
/// consumers that take work concurrently (<c>seda</c> with concurrent consumers, <c>file</c>, <c>timer</c>).
/// </summary>
public sealed class ThrottlingInflightRoutePolicy : RoutePolicySupport
{
    private readonly Lock _gate = new();
    private bool _suspended;

    /// <summary>The maximum number of inflight exchanges before the consumer is suspended (default 1000, as Camel).</summary>
    public int MaxInflightExchanges { get; set; } = 1000;

    /// <summary>The percentage of <see cref="MaxInflightExchanges"/> at or below which the consumer is resumed (default 70).</summary>
    public int ResumePercentOfMax { get; set; } = 70;

    /// <summary>Whether the route's or the context's inflight exchanges are counted (default <see cref="ThrottlingScope.Route"/>).</summary>
    public ThrottlingScope Scope { get; set; } = ThrottlingScope.Route;

    /// <summary>The level at which suspending and resuming is logged (default <see cref="LogLevel.Information"/>).</summary>
    public LogLevel LoggingLevel { get; set; } = LogLevel.Information;

    /// <summary>The inflight count at or below which the consumer is resumed.</summary>
    public int ResumeInflightExchanges => (int)(MaxInflightExchanges * (ResumePercentOfMax / 100.0));

    /// <inheritdoc />
    public override void OnExchangeDone(IRoute route, Exchange exchange)
    {
        var size = Size(route, exchange);
        lock (_gate)
        {
            if (MaxInflightExchanges > 0 && size > MaxInflightExchanges)
            {
                if (!_suspended)
                {
                    _suspended = true;
                    Log(route, "Throttling consumer: {Size} > {Max} inflight exchanges by suspending consumer", size, MaxInflightExchanges);
                    _ = SuspendConsumerAsync(route);
                }
            }
            else if (_suspended && size <= ResumeInflightExchanges)
            {
                _suspended = false;
                Log(route, "Throttling consumer: {Size} <= {Max} inflight exchanges by resuming consumer", size, ResumeInflightExchanges);
                _ = ResumeConsumerAsync(route);
            }
        }
    }

    /// <inheritdoc />
    public override void OnStart(IRoute route)
    {
        lock (_gate)
        {
            _suspended = false;
        }
    }

    /// <inheritdoc />
    public override string ToString() => $"ThrottlingInflightRoutePolicy[{MaxInflightExchanges} / {ResumePercentOfMax}% using scope {Scope}]";

    private int Size(IRoute route, Exchange exchange) =>
        Scope == ThrottlingScope.Context ? exchange.Context.InflightRepository.Size : route.InflightCount;

    private void Log(IRoute route, string message, int size, int threshold) =>
        route.Endpoint.Context.LoggerFactory.CreateLogger<ThrottlingInflightRoutePolicy>().Log(LoggingLevel, message, size, threshold);
}
