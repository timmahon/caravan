namespace Caravan.Spi;

/// <summary>An event raised by the context.</summary>
public sealed record CaravanEvent(CaravanEventType Type, ICaravanContext Context)
{
    /// <summary>The exchange the event is about, if any.</summary>
    public Exchange? Exchange { get; init; }

    /// <summary>The id of the route the event is about, if any.</summary>
    public string? RouteId { get; init; }

    /// <summary>The endpoint involved, if any.</summary>
    public IEndpoint? Endpoint { get; init; }

    /// <summary>For <see cref="CaravanEventType.ExchangeSent"/>: how long sending took.</summary>
    public TimeSpan? Duration { get; init; }

    /// <summary>For failure events (such as <see cref="CaravanEventType.RouteRestartingFailure"/>): the cause.</summary>
    public Exception? Cause { get; init; }

    /// <summary>For <see cref="CaravanEventType.RouteRestarting"/> and <see cref="CaravanEventType.RouteRestartingFailure"/>: the attempt number.</summary>
    public int? Attempt { get; init; }
}
