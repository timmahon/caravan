using Caravan.Model;
using Caravan.Processors;
using Caravan.Spi;
using Caravan.Support;

namespace Caravan.Impl;

/// <summary>A runtime route: a consumer feeding a processor tree.</summary>
public sealed class Route : ServiceSupport, IRoute
{
    private int _inflight;

    internal Route(RouteContext routeContext, IEndpoint endpoint, IProcessor pipeline)
    {
        RouteContext = routeContext;
        Endpoint = endpoint;
        Processor = new RouteProcessor(this, pipeline);
    }

    public string Id => RouteContext.RouteId;

    public RouteContext RouteContext { get; }

    public RouteDefinition Definition => RouteContext.Definition;

    public IEndpoint Endpoint { get; }

    public IConsumer? Consumer { get; private set; }

    /// <summary>The entry processor: unit of work, inflight tracking, then the route's steps.</summary>
    public IProcessor Processor { get; }

    public int InflightCount => Volatile.Read(ref _inflight);

    public IDictionary<string, object?> Properties { get; } = new Dictionary<string, object?>(StringComparer.Ordinal);

    internal void IncrementInflight() => Interlocked.Increment(ref _inflight);

    internal void DecrementInflight() => Interlocked.Decrement(ref _inflight);

    protected override async Task DoStartAsync(CancellationToken cancellationToken)
    {
        foreach (var service in RouteContext.Services)
        {
            await StartServiceAsync(service, cancellationToken).ConfigureAwait(false);
        }

        await Endpoint.StartAsync(cancellationToken).ConfigureAwait(false);
        Consumer = Endpoint.CreateConsumer(Processor);
        await Consumer.StartAsync(cancellationToken).ConfigureAwait(false);
    }

    protected override async Task DoStopAsync(CancellationToken cancellationToken)
    {
        if (Consumer is not null)
        {
            await Consumer.StopAsync(cancellationToken).ConfigureAwait(false);
            Consumer = null;
        }

        foreach (var service in RouteContext.Services.Reverse())
        {
            await StopServiceAsync(service, cancellationToken).ConfigureAwait(false);
        }
    }

    protected override Task DoSuspendAsync(CancellationToken cancellationToken) =>
        Consumer is ISuspendableService s ? s.SuspendAsync(cancellationToken) : StopServiceAsync(Consumer, cancellationToken);

    protected override async Task DoResumeAsync(CancellationToken cancellationToken)
    {
        if (Consumer is ISuspendableService { Status: ServiceStatus.Suspended } s)
        {
            await s.ResumeAsync(cancellationToken).ConfigureAwait(false);
        }
        else if (Consumer is not null)
        {
            await Consumer.StartAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public override string ToString() => $"Route[{Id}: {Endpoint.EndpointUri}]";
}

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

/// <summary>Default <see cref="IUnitOfWork"/>.</summary>
public sealed class DefaultUnitOfWork : IUnitOfWork
{
    private readonly List<ISynchronization> _synchronizations = [];

    public DefaultUnitOfWork(Exchange exchange)
    {
        Exchange = exchange;
        if (exchange.Context.AllowUseOriginalMessage)
        {
            OriginalMessage = exchange.Message.Copy(exchange);
        }
    }

    public Exchange Exchange { get; }

    public Message? OriginalMessage { get; }

    public void AddSynchronization(ISynchronization synchronization)
    {
        lock (_synchronizations)
        {
            _synchronizations.Add(synchronization);
        }
    }

    public bool ContainsSynchronization(ISynchronization synchronization)
    {
        lock (_synchronizations)
        {
            return _synchronizations.Contains(synchronization);
        }
    }

    public void RemoveSynchronization(ISynchronization synchronization)
    {
        lock (_synchronizations)
        {
            _synchronizations.Remove(synchronization);
        }
    }

    public void HandoverSynchronization(Exchange target, Func<ISynchronization, bool>? filter = null)
    {
        List<ISynchronization> moved;
        lock (_synchronizations)
        {
            moved = _synchronizations.Where(s => filter?.Invoke(s) ?? true).ToList();
            foreach (var s in moved)
            {
                _synchronizations.Remove(s);
            }
        }

        target.UnitOfWork ??= new DefaultUnitOfWork(target);
        foreach (var s in moved)
        {
            target.UnitOfWork.AddSynchronization(s);
        }
    }

    public async ValueTask DoneAsync(Exchange exchange)
    {
        List<ISynchronization> snapshot;
        lock (_synchronizations)
        {
            snapshot = [.. _synchronizations];
            _synchronizations.Clear();
        }

        foreach (var synchronization in snapshot)
        {
            try
            {
                if (exchange.IsFailed || exchange.IsRollbackOnly)
                {
                    await synchronization.OnFailureAsync(exchange).ConfigureAwait(false);
                }
                else
                {
                    await synchronization.OnCompleteAsync(exchange).ConfigureAwait(false);
                }
            }
            catch
            {
                // A failing synchronization must not affect the others.
            }
        }
    }
}
