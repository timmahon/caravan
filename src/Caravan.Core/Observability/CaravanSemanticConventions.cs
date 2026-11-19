namespace Caravan.Observability;

/// <summary>Names of the span attributes, metric instruments and propagation headers used by Caravan's telemetry.</summary>
public static class CaravanSemanticConventions
{
    /// <summary>Span and metric attribute: the context name.</summary>
    public const string ContextName = "caravan.context.name";

    /// <summary>Span and metric attribute: the route id.</summary>
    public const string RouteId = "caravan.route.id";

    /// <summary>Span attribute: the exchange id.</summary>
    public const string ExchangeId = "caravan.exchange.id";

    /// <summary>Span and metric attribute: the endpoint URI, without secrets (see <see cref="UriSanitizer"/>).</summary>
    public const string EndpointUri = "caravan.endpoint.uri";

    /// <summary>Span attribute: the node id (node spans).</summary>
    public const string NodeId = "caravan.node.id";

    /// <summary>Span attribute: the node label such as <c>to[mock:a]</c> (node spans).</summary>
    public const string NodeLabel = "caravan.node.label";

    /// <summary>Span attribute: the exchange pattern (<c>InOnly</c>/<c>InOut</c>).</summary>
    public const string ExchangePattern = "caravan.exchange.pattern";

    /// <summary>OpenTelemetry messaging attribute: the component scheme (<c>seda</c>, <c>direct</c>, ...).</summary>
    public const string MessagingSystem = "messaging.system";

    /// <summary>OpenTelemetry messaging attribute: the endpoint path (without options).</summary>
    public const string MessagingDestinationName = "messaging.destination.name";

    /// <summary>OpenTelemetry messaging attribute: <c>send</c> for producer spans, <c>process</c> for route spans.</summary>
    public const string MessagingOperationType = "messaging.operation.type";

    /// <summary>OpenTelemetry messaging attribute: the message id.</summary>
    public const string MessagingMessageId = "messaging.message.id";

    /// <summary>The W3C trace context header carrying the parent span (<c>traceparent</c>).</summary>
    public const string TraceParentHeader = "traceparent";

    /// <summary>The W3C trace context header carrying vendor state (<c>tracestate</c>).</summary>
    public const string TraceStateHeader = "tracestate";

    /// <summary>The W3C baggage header (<c>baggage</c>).</summary>
    public const string BaggageHeader = "baggage";

    /// <summary>Counter: exchanges entering a route (each entry counts, also through <c>direct</c>).</summary>
    public const string ExchangesTotal = "caravan.exchanges.total";

    /// <summary>Counter: exchanges that left a route successfully.</summary>
    public const string ExchangesSucceeded = "caravan.exchanges.succeeded";

    /// <summary>Counter: exchanges that left a route failed.</summary>
    public const string ExchangesFailed = "caravan.exchanges.failed";

    /// <summary>Counter: failures handled by an error handler (dead letter channel, <c>onException</c>).</summary>
    public const string ExchangesFailuresHandled = "caravan.exchanges.failures_handled";

    /// <summary>Counter: redelivery attempts made by error handlers.</summary>
    public const string ExchangesRedeliveries = "caravan.exchanges.redeliveries";

    /// <summary>Up-down counter: exchanges currently in a route.</summary>
    public const string ExchangesInflight = "caravan.exchanges.inflight";

    /// <summary>Histogram (seconds): time an exchange spent in a route.</summary>
    public const string ExchangeDuration = "caravan.exchange.duration";

    /// <summary>Observable gauge: started routes per context.</summary>
    public const string RoutesRunning = "caravan.routes.running";

    /// <summary>Histogram (seconds): time a send to an endpoint took.</summary>
    public const string SendDuration = "caravan.send.duration";
}
