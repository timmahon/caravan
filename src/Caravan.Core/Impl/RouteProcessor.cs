using Caravan.Processors;
using Caravan.Spi;

namespace Caravan.Impl;

/// <summary>
/// The first processor of every route: assigns the route id, opens a unit of work when the exchange
/// does not have one yet, tracks inflight exchanges and completes the unit of work at the end.
/// </summary>
internal sealed class RouteProcessor(Route route, IProcessor pipeline) : INavigableProcessor
{
    public IReadOnlyList<IProcessor> Next => [pipeline];

    public async ValueTask ProcessAsync(Exchange exchange)
    {
        var context = exchange.Context;
        exchange.FromRouteId ??= route.Id;
        exchange.FromEndpoint ??= route.Endpoint;
        if (exchange.CancellationToken == default)
        {
            exchange.CancellationToken = context.ShutdownToken;
        }

        var unitOfWork = exchange.UnitOfWork is null ? new DefaultUnitOfWork(exchange) : null;
        if (unitOfWork is not null)
        {
            exchange.UnitOfWork = unitOfWork;
            EventHelper.Notify(context, CaravanEventType.ExchangeCreated, exchange, route.Endpoint, route.Id);
        }

        route.IncrementInflight();
        try
        {
            await pipeline.ProcessAsync(exchange).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            exchange.Exception = e;
        }
        finally
        {
            route.DecrementInflight();
            if (unitOfWork is not null)
            {
                await unitOfWork.DoneAsync(exchange).ConfigureAwait(false);
                if (ReferenceEquals(exchange.UnitOfWork, unitOfWork))
                {
                    exchange.UnitOfWork = null;
                }

                EventHelper.Notify(
                    context,
                    exchange.IsFailed ? CaravanEventType.ExchangeFailed : CaravanEventType.ExchangeCompleted,
                    exchange,
                    route.Endpoint,
                    route.Id);
            }
        }
    }

    public override string ToString() => $"RouteProcessor[{route.Id}]";
}
