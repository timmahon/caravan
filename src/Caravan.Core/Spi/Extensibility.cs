using Caravan.Model;

namespace Caravan.Spi;

/// <summary>
/// Wraps the processor of each node in a route. Used for tracing, metrics, message history,
/// interception and testing advice (Camel's <c>InterceptStrategy</c>).
/// </summary>
public interface IInterceptStrategy
{
    IProcessor WrapProcessor(RouteContext routeContext, ProcessorDefinition definition, IProcessor target);
}

/// <summary>Wraps a node's processor with error handling (redelivery, dead letter channel, ...).</summary>
public interface IErrorHandlerFactory
{
    IProcessor Wrap(RouteContext routeContext, ProcessorDefinition definition, IProcessor target);
}

/// <summary>Resolves property placeholder values.</summary>
public interface IPropertiesSource
{
    string? GetProperty(string key);
}

/// <summary>A properties source backed by a dictionary.</summary>
public sealed class DictionaryPropertiesSource(IDictionary<string, string?> properties) : IPropertiesSource
{
    public DictionaryPropertiesSource()
        : this(new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase))
    {
    }

    public IDictionary<string, string?> Properties { get; } = properties;

    public string? GetProperty(string key) => Properties.TryGetValue(key, out var value) ? value : null;
}

/// <summary>Kinds of events published to <see cref="IEventNotifier"/>s.</summary>
public enum CaravanEventType
{
    ContextStarting,
    ContextStarted,
    ContextStopping,
    ContextStopped,
    RouteAdded,
    RouteStarted,
    RouteStopped,
    RouteRemoved,
    ExchangeCreated,
    ExchangeCompleted,
    ExchangeFailed,
    ExchangeSending,
    ExchangeSent,
    ExchangeRedelivery,
    ExchangeFailureHandled,
}

/// <summary>An event raised by the context.</summary>
public sealed record CaravanEvent(CaravanEventType Type, ICaravanContext Context)
{
    public Exchange? Exchange { get; init; }

    public string? RouteId { get; init; }

    public IEndpoint? Endpoint { get; init; }

    /// <summary>For <see cref="CaravanEventType.ExchangeSent"/>: how long sending took.</summary>
    public TimeSpan? Duration { get; init; }
}

/// <summary>Receives context events (Camel's <c>EventNotifier</c>).</summary>
public interface IEventNotifier
{
    bool IsEnabled(CaravanEvent evt) => true;

    void Notify(CaravanEvent evt);
}

/// <summary>A runtime route.</summary>
public interface IRoute
{
    string Id { get; }

    RouteDefinition Definition { get; }

    IEndpoint Endpoint { get; }

    IConsumer? Consumer { get; }

    IProcessor Processor { get; }

    ServiceStatus Status { get; }

    /// <summary>Number of exchanges currently being routed.</summary>
    int InflightCount { get; }

    IDictionary<string, object?> Properties { get; }
}
