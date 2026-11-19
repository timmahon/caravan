namespace Caravan.Observability;

/// <summary>
/// One traced step kept by the <see cref="BacklogTracer"/> (Camel's <c>BacklogTracerEventMessage</c>): the message as it reached a node (or
/// the route input), then how long the node took and the exception it left, once it finished.
/// </summary>
public sealed class BacklogTracerEventMessage
{
    private long _elapsedTicks = -1;
    private volatile string? _exceptionAsText;

    internal BacklogTracerEventMessage(
        long uid,
        DateTimeOffset timestamp,
        string routeId,
        string? nodeId,
        string? label,
        string exchangeId,
        string? body,
        string? bodyType,
        IReadOnlyDictionary<string, string?> headers)
    {
        Uid = uid;
        Timestamp = timestamp;
        RouteId = routeId;
        NodeId = nodeId;
        Label = label;
        ExchangeId = exchangeId;
        Body = body;
        BodyType = bodyType;
        Headers = headers;
    }

    /// <summary>A sequence number unique within the tracer.</summary>
    public long Uid { get; }

    /// <summary>When the exchange reached the step.</summary>
    public DateTimeOffset Timestamp { get; }

    /// <summary>The route id.</summary>
    public string RouteId { get; }

    /// <summary>The node id; null for the route input.</summary>
    public string? NodeId { get; }

    /// <summary>The node label (such as <c>to[mock:a]</c>); null for the route input.</summary>
    public string? Label { get; }

    /// <summary>Whether this is the route input rather than a node.</summary>
    public bool IsRouteInput => NodeId is null;

    /// <summary>The exchange id.</summary>
    public string ExchangeId { get; }

    /// <summary>The body as text (truncated to <see cref="BacklogTracer.BodyMaxChars"/>), or null.</summary>
    public string? Body { get; }

    /// <summary>The body's type name, or null.</summary>
    public string? BodyType { get; }

    /// <summary>The headers as text.</summary>
    public IReadOnlyDictionary<string, string?> Headers { get; }

    /// <summary>How long the node took; null while it is running.</summary>
    public TimeSpan? Elapsed
    {
        get
        {
            var ticks = Interlocked.Read(ref _elapsedTicks);
            return ticks < 0 ? null : TimeSpan.FromTicks(ticks);
        }
    }

    /// <summary>The exception the node left on the exchange (type and message, with stack trace), or null.</summary>
    public string? ExceptionAsText => _exceptionAsText;

    /// <inheritdoc />
    public override string ToString() => $"#{Uid} {RouteId}/{NodeId ?? "from"} {ExchangeId}: {Body}";

    internal void Done(TimeSpan elapsed, Exception? exception)
    {
        _exceptionAsText = exception?.ToString();
        Interlocked.Exchange(ref _elapsedTicks, Math.Max(0, elapsed.Ticks));
    }
}
