using Caravan.Spi;
using Microsoft.Extensions.Logging;

namespace Caravan.RoutePolicies;

/// <summary>
/// A circuit breaker on a route's consumer (Camel's <c>ThrottlingExceptionRoutePolicy</c>): when <see cref="FailureThreshold"/> exchanges
/// failed (with one of <see cref="ThrottledExceptions"/>, or any exception) within <see cref="FailureWindow"/>, the circuit opens and the consumer
/// is suspended, so no more messages are taken from a failing source. After <see cref="HalfOpenAfter"/> (on the context's clock) the circuit
/// half-opens — if <see cref="HalfOpenHandler"/> agrees — and the consumer resumes: the next exchange done closes the circuit when it succeeds and
/// opens it again when it fails. <see cref="KeepOpen"/> forces the circuit open. Only failures the exchange still carries count (a failure
/// handled by an error handler does not).
/// </summary>
public sealed class ThrottlingExceptionRoutePolicy : RoutePolicySupport, IDisposable
{
    private readonly Lock _gate = new();
    private readonly Queue<DateTimeOffset> _failures = new();
    private IRoute? _route;
    private ITimer? _timer;
    private bool _keepOpen;

    /// <summary>The number of failures within <see cref="FailureWindow"/> that opens the circuit (default 50, as Camel).</summary>
    public int FailureThreshold { get; set; } = 50;

    /// <summary>How far back failures are counted (default 60 s).</summary>
    public TimeSpan FailureWindow { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>How long the circuit stays open before it half-opens (default 30 s).</summary>
    public TimeSpan HalfOpenAfter { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Decides whether the circuit may half-open; null always lets it.</summary>
    public IThrottlingExceptionHalfOpenHandler? HalfOpenHandler { get; set; }

    /// <summary>The exception types that count as failures (their subclasses too); empty counts every exception.</summary>
    public IList<Type> ThrottledExceptions { get; } = [];

    /// <summary>The current state.</summary>
    public ThrottlingExceptionState State { get; private set; } = ThrottlingExceptionState.Closed;

    /// <summary>The number of failures currently counted.</summary>
    public int Failures
    {
        get
        {
            lock (_gate)
            {
                return _failures.Count;
            }
        }
    }

    /// <summary>Forces the circuit open (true) or lets it close again (false; it then half-opens after <see cref="HalfOpenAfter"/>).</summary>
    public bool KeepOpen
    {
        get => _keepOpen;
        set
        {
            lock (_gate)
            {
                _keepOpen = value;
                if (value && _route is { } route && State != ThrottlingExceptionState.Open)
                {
                    Open(route);
                }
            }
        }
    }

    /// <inheritdoc />
    public override void OnInit(IRoute route)
    {
        lock (_gate)
        {
            _route = route;
        }
    }

    /// <inheritdoc />
    public override void OnStart(IRoute route)
    {
        lock (_gate)
        {
            _route = route;
            if (_keepOpen && State != ThrottlingExceptionState.Open)
            {
                Open(route);
            }
        }
    }

    /// <inheritdoc />
    public override void OnStop(IRoute route)
    {
        lock (_gate)
        {
            _timer?.Dispose();
            _timer = null;
            _failures.Clear();
            State = ThrottlingExceptionState.Closed;
        }
    }

    /// <inheritdoc />
    public override void OnExchangeDone(IRoute route, Exchange exchange)
    {
        var failed = HasFailed(exchange);
        var now = exchange.Context.TimeProvider.GetUtcNow();
        lock (_gate)
        {
            if (_keepOpen)
            {
                return;
            }

            switch (State)
            {
                case ThrottlingExceptionState.Closed:
                    if (failed)
                    {
                        _failures.Enqueue(now);
                    }

                    while (_failures.Count > 0 && _failures.Peek() < now - FailureWindow)
                    {
                        _failures.Dequeue();
                    }

                    if (_failures.Count >= FailureThreshold)
                    {
                        Open(route);
                    }

                    break;
                case ThrottlingExceptionState.HalfOpen:
                    if (failed)
                    {
                        Open(route);
                    }
                    else
                    {
                        Log(route, LogLevel.Information, "Closing circuit of route {RouteId}");
                        _failures.Clear();
                        State = ThrottlingExceptionState.Closed;
                    }

                    break;
            }
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_gate)
        {
            _timer?.Dispose();
            _timer = null;
        }
    }

    /// <inheritdoc />
    public override string ToString() => $"ThrottlingExceptionRoutePolicy[{State}, threshold {FailureThreshold} in {FailureWindow}, half-open after {HalfOpenAfter}]";

    private bool HasFailed(Exchange exchange)
    {
        if (exchange.Exception is not { } exception)
        {
            return false;
        }

        if (ThrottledExceptions.Count == 0)
        {
            return true;
        }

        for (var e = exception; e is not null; e = e.InnerException)
        {
            if (ThrottledExceptions.Any(t => t.IsInstanceOfType(e)))
            {
                return true;
            }
        }

        return false;
    }

    private void Open(IRoute route)
    {
        Log(route, LogLevel.Warning, "Opening circuit of route {RouteId}: suspending its consumer");
        State = ThrottlingExceptionState.Open;
        _ = SuspendConsumerAsync(route);
        ScheduleHalfOpen(route);
    }

    private void ScheduleHalfOpen(IRoute route)
    {
        _timer?.Dispose();
        _timer = route.Endpoint.Context.TimeProvider.CreateTimer(_ => HalfOpen(route), null, HalfOpenAfter, Timeout.InfiniteTimeSpan);
    }

    private void HalfOpen(IRoute route)
    {
        lock (_gate)
        {
            if (State != ThrottlingExceptionState.Open || _keepOpen)
            {
                if (_keepOpen)
                {
                    ScheduleHalfOpen(route);
                }

                return;
            }

            bool ready;
            try
            {
                ready = HalfOpenHandler?.IsReadyToBeClosed() ?? true;
            }
            catch (Exception e)
            {
                route.Endpoint.Context.LoggerFactory.CreateLogger<ThrottlingExceptionRoutePolicy>().LogWarning(e, "Half-open handler failed");
                ready = false;
            }

            if (!ready)
            {
                ScheduleHalfOpen(route);
                return;
            }

            Log(route, LogLevel.Information, "Half-opening circuit of route {RouteId}: resuming its consumer");
            State = ThrottlingExceptionState.HalfOpen;
            _ = ResumeConsumerAsync(route);
        }
    }

    private static void Log(IRoute route, LogLevel level, string message) =>
        route.Endpoint.Context.LoggerFactory.CreateLogger<ThrottlingExceptionRoutePolicy>().Log(level, message, route.Id);
}
