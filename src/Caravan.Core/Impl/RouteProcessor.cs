using Caravan.Management;
using Caravan.Observability;
using Caravan.Processors;
using Caravan.Spi;

namespace Caravan.Impl;

/// <summary>
/// The first processor of every route: assigns the route id, opens a unit of work when the exchange
/// does not have one yet, tracks inflight exchanges (route counter and <see cref="ICaravanContext.InflightRepository"/>)
/// and completes the unit of work at the end. It also records the route's statistics, span, metrics, message history and trace.
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

        // The route's local beans (route templates) are visible to registry lookups on this flow; other routes see none.
        var previousLocalBeans = LocalBeanRegistry.Current;
        var localBeans = route.Definition.LocalBeans;
        if (!ReferenceEquals(previousLocalBeans, localBeans))
        {
            LocalBeanRegistry.Current = localBeans;
        }

        var clock = context.TimeProvider;
        var start = clock.GetTimestamp();
        var now = clock.GetUtcNow();
        var management = context.Management as DefaultManagementService;
        var contextCounters = unitOfWork is not null && management is { ContextStatisticsEnabled: true } ? management.ContextCounters : null;
        contextCounters?.Begin(now);
        route.EndpointUsage?.IncrementInbound();
        var activity = RouteTelemetry.StartActivity(route, exchange, unitOfWork is not null);
        var metrics = RouteTelemetry.RecordStart(context, route.Id);
        var history = context.MessageHistory
            ? MessageHistoryRecorder.Add(exchange, route.Id, route.Id, "from[" + route.TelemetryInfo.SanitizedUri + "]", now)
            : null;
        context.Tracer.TraceRoute(exchange, route.Id, route.Endpoint.EndpointUri, input: true);
        var traced = context.BacklogTracer.Trace(exchange, route.Id, null);

        var previousRouteId = exchange.CurrentRouteId;
        exchange.CurrentRouteId = route.Id;
        var inflight = context.InflightRepository;
        route.IncrementInflight(now);
        inflight.Add(exchange, route.Id);
        var policies = route.RoutePolicies.Count > 0;
        if (policies)
        {
            route.NotifyPolicies((policy, r) => policy.OnExchangeBegin(r, exchange));
        }

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
            exchange.CurrentRouteId = previousRouteId;
            if (!ReferenceEquals(previousLocalBeans, localBeans))
            {
                LocalBeanRegistry.Current = previousLocalBeans;
            }

            if (unitOfWork is null)
            {
                // markRollbackOnlyLast only rolls back the current route: the calling route continues.
                exchange.IsRollbackOnlyLast = false;
            }

            try
            {
                if (unitOfWork is not null)
                {
                    await unitOfWork.DoneAsync(exchange).ConfigureAwait(false);
                    if (ReferenceEquals(exchange.UnitOfWork, unitOfWork))
                    {
                        exchange.UnitOfWork = null;
                    }
                }
            }
            finally
            {
                // The unit of work's callbacks are part of the exchange's work: count them as inflight.
                var failed = exchange.IsFailed;
                var elapsed = clock.GetElapsedTime(start);
                var end = clock.GetUtcNow();
                route.DecrementInflight(failed, elapsed, end);
                inflight.Remove(exchange, route.Id);
                contextCounters?.End(elapsed, failed, end);
                history?.Done(elapsed);
                traced?.Done(elapsed, exchange.Exception);
                if (metrics)
                {
                    RouteTelemetry.RecordEnd(context, route.Id, failed, elapsed);
                }

                context.Tracer.TraceRoute(exchange, route.Id, route.Endpoint.EndpointUri, input: false);
                if (activity is not null)
                {
                    RouteTelemetry.StopActivity(activity, exchange);
                }

                if (policies)
                {
                    route.NotifyPolicies((policy, r) => policy.OnExchangeDone(r, exchange));
                }
            }

            if (unitOfWork is not null)
            {
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
