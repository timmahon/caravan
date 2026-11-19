using Caravan.Spi;

namespace Caravan.Processors;

/// <summary>Helpers for raising events.</summary>
public static class EventHelper
{
    /// <summary>Raises an event of <paramref name="type"/> when the context has event notifiers.</summary>
    public static void Notify(ICaravanContext context, CaravanEventType type, Exchange? exchange = null, IEndpoint? endpoint = null, string? routeId = null)
    {
        if (context.EventNotifiers.Count == 0)
        {
            return;
        }

        Notify(context, new CaravanEvent(type, context) { Exchange = exchange, Endpoint = endpoint, RouteId = routeId });
    }

    /// <summary>Sends <paramref name="evt"/> to the enabled notifiers; notifier failures are ignored.</summary>
    public static void Notify(ICaravanContext context, CaravanEvent evt)
    {
        foreach (var notifier in context.EventNotifiers)
        {
            try
            {
                if (notifier.IsEnabled(evt))
                {
                    notifier.Notify(evt);
                }
            }
            catch
            {
                // Event notifiers must never break routing.
            }
        }
    }
}
