using Caravan.Spi;
using Caravan.Support;

namespace Caravan.Components.Kamelet;

/// <summary>
/// Consumes from a Kamelet (<c>from("kamelet:templateId?...")</c>): registers the route's processor as the Kamelet's sink and creates (or
/// restarts) the Kamelet's route; stopping the consumer stops that route.
/// </summary>
internal sealed class KameletConsumer(KameletEndpoint endpoint, IProcessor processor) : ConsumerBase(endpoint, processor)
{
    private string? _routeId;

    protected override async Task DoStartAsync(CancellationToken cancellationToken)
    {
        string? registered = null;
        string routeId;
        try
        {
            routeId = await endpoint.EnsureRouteAsync(
                id =>
                {
                    endpoint.KameletComponent.AddSink(id, Processor);
                    registered = id;
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch when (registered is not null)
        {
            endpoint.KameletComponent.RemoveSink(registered, Processor);
            throw;
        }

        _routeId = routeId;
        if (registered is null)
        {
            endpoint.KameletComponent.AddSink(routeId, Processor);
        }
        if (endpoint.Context.GetRouteStatus(routeId) is ServiceStatus.Stopped or ServiceStatus.Initialized)
        {
            await endpoint.Context.StartRouteAsync(routeId, cancellationToken).ConfigureAwait(false);
        }
    }

    protected override async Task DoStopAsync(CancellationToken cancellationToken)
    {
        if (_routeId is not { } routeId)
        {
            return;
        }

        if (endpoint.Context.GetRouteStatus(routeId) is ServiceStatus.Started or ServiceStatus.Suspended &&
            endpoint.Context.Status is ServiceStatus.Started)
        {
            await endpoint.Context.StopRouteAsync(routeId, cancellationToken).ConfigureAwait(false);
        }

        endpoint.KameletComponent.RemoveSink(routeId, Processor);
    }
}
