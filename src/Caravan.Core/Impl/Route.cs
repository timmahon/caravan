using Caravan.Model;
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
        TelemetryInfo = new Observability.EndpointTelemetryInfo(endpoint);
    }

    /// <inheritdoc />
    public string Id => RouteContext.RouteId;

    /// <summary>The state used to build the route; owns its services.</summary>
    public RouteContext RouteContext { get; }

    /// <inheritdoc />
    public RouteDefinition Definition => RouteContext.Definition;

    /// <inheritdoc />
    public IEndpoint Endpoint { get; }

    /// <inheritdoc />
    public IConsumer? Consumer { get; private set; }

    /// <summary>The entry processor: unit of work, inflight tracking, then the route's steps.</summary>
    public IProcessor Processor { get; }

    /// <summary>The number of exchanges currently being processed by the route.</summary>
    public int InflightCount => Volatile.Read(ref _inflight);

    /// <inheritdoc />
    public IDictionary<string, object?> Properties { get; } = new Dictionary<string, object?>(StringComparer.Ordinal);

    /// <summary>The number of exchanges the route has processed or is processing (each entry counts, also through <c>direct</c>).</summary>
    public long ExchangesTotal => RouteContext.Counters.Total;

    /// <summary>The number of exchanges that left the route successfully.</summary>
    public long ExchangesCompleted => RouteContext.Counters.Completed;

    /// <summary>The number of exchanges that left the route failed.</summary>
    public long ExchangesFailed => RouteContext.Counters.Failed;

    /// <summary>When the route was last started; null while it is not started.</summary>
    internal DateTimeOffset? StartedAt { get; private set; }

    /// <summary>What spans show about the route's endpoint.</summary>
    internal Observability.EndpointTelemetryInfo TelemetryInfo { get; }

    /// <summary>The usage of the route's endpoint (management).</summary>
    internal Management.EndpointUsage? EndpointUsage { get; init; }

    internal void IncrementInflight(DateTimeOffset now)
    {
        Interlocked.Increment(ref _inflight);
        RouteContext.Counters.Begin(now);
    }

    internal void DecrementInflight(bool failed, TimeSpan elapsed, DateTimeOffset now)
    {
        RouteContext.Counters.End(elapsed, failed, now);
        Interlocked.Decrement(ref _inflight);
    }

    /// <summary>Starts the route's services, then its consumer.</summary>
    protected override async Task DoStartAsync(CancellationToken cancellationToken)
    {
        StartedAt = null;
        foreach (var service in RouteContext.Services)
        {
            await StartServiceAsync(service, cancellationToken).ConfigureAwait(false);
        }

        await Endpoint.StartAsync(cancellationToken).ConfigureAwait(false);
        Consumer = Endpoint.CreateConsumer(Processor);
        await Consumer.StartAsync(cancellationToken).ConfigureAwait(false);
        StartedAt = RouteContext.Context.TimeProvider.GetUtcNow();
    }

    /// <summary>Stops the consumer first, then the route's services in reverse order.</summary>
    protected override async Task DoStopAsync(CancellationToken cancellationToken)
    {
        StartedAt = null;
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

    /// <summary>Suspends the consumer (stops it when it cannot be suspended).</summary>
    protected override Task DoSuspendAsync(CancellationToken cancellationToken) =>
        Consumer is ISuspendableService s ? s.SuspendAsync(cancellationToken) : StopServiceAsync(Consumer, cancellationToken);

    /// <summary>Resumes (or restarts) the consumer.</summary>
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

    /// <inheritdoc />
    public override string ToString() => $"Route[{Id}: {Endpoint.EndpointUri}]";
}
