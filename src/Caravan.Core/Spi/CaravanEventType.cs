namespace Caravan.Spi;

/// <summary>Kinds of events published to <see cref="IEventNotifier"/>s.</summary>
public enum CaravanEventType
{
    /// <summary>The context is starting.</summary>
    ContextStarting,

    /// <summary>The context has started.</summary>
    ContextStarted,

    /// <summary>The context is stopping.</summary>
    ContextStopping,

    /// <summary>The context has stopped.</summary>
    ContextStopped,

    /// <summary>A route was added.</summary>
    RouteAdded,

    /// <summary>A route was started.</summary>
    RouteStarted,

    /// <summary>A route was stopped.</summary>
    RouteStopped,

    /// <summary>A route was removed.</summary>
    RouteRemoved,

    /// <summary>A route received a new exchange (a new unit of work was created).</summary>
    ExchangeCreated,

    /// <summary>An exchange completed successfully.</summary>
    ExchangeCompleted,

    /// <summary>An exchange completed with an exception.</summary>
    ExchangeFailed,

    /// <summary>An exchange is about to be sent to an endpoint.</summary>
    ExchangeSending,

    /// <summary>An exchange was sent to an endpoint (see <see cref="CaravanEvent.Duration"/>).</summary>
    ExchangeSent,

    /// <summary>An exchange is being redelivered by an error handler.</summary>
    ExchangeRedelivery,

    /// <summary>An error handler handled a failed exchange.</summary>
    ExchangeFailureHandled,
}
