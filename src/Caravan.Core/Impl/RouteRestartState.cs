namespace Caravan.Impl;

/// <summary>The state of a route the <see cref="SupervisingRouteController"/> is trying to (re)start.</summary>
public sealed class RouteRestartState
{
    internal RouteRestartState(string routeId)
    {
        RouteId = routeId;
    }

    /// <summary>The route id.</summary>
    public string RouteId { get; }

    /// <summary>The number of restart attempts made so far (the initial start is not counted).</summary>
    public int Attempts { get; internal set; }

    /// <summary>The last failure.</summary>
    public Exception? LastError { get; internal set; }

    /// <summary>When the next attempt is due, while one is scheduled.</summary>
    public DateTimeOffset? NextAttempt { get; internal set; }

    /// <summary>True when <see cref="SupervisingRouteController.BackOffMaxAttempts"/> was reached: no more attempts are made.</summary>
    public bool IsExhausted { get; internal set; }

    /// <inheritdoc />
    public override string ToString() =>
        $"RouteRestartState[{RouteId}: attempts={Attempts}, exhausted={IsExhausted}, error={LastError?.Message}]";
}
