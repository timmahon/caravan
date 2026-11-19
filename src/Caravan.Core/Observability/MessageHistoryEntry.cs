namespace Caravan.Observability;

/// <summary>
/// One step of an exchange's message history (Camel's <c>MessageHistory</c>): the route input or a node the exchange went through. The
/// history is the <see cref="ExchangePropertyKeys.MessageHistory"/> property (a <c>List&lt;MessageHistoryEntry&gt;</c>) when
/// <see cref="ICaravanContext.MessageHistory"/> is enabled.
/// </summary>
public sealed class MessageHistoryEntry
{
    private long _elapsedTicks = -1;

    /// <summary>Creates an entry.</summary>
    /// <param name="routeId">The route id.</param>
    /// <param name="nodeId">The node id (the route id for the route input).</param>
    /// <param name="label">The processor label, such as <c>to[mock:a]</c> or <c>from[direct:a]</c>.</param>
    /// <param name="timestamp">When the exchange reached the step.</param>
    public MessageHistoryEntry(string routeId, string nodeId, string label, DateTimeOffset timestamp)
    {
        RouteId = routeId;
        NodeId = nodeId;
        Label = label;
        Timestamp = timestamp;
    }

    /// <summary>The route id.</summary>
    public string RouteId { get; }

    /// <summary>The node id (the route id for the route input).</summary>
    public string NodeId { get; }

    /// <summary>The processor label.</summary>
    public string Label { get; }

    /// <summary>When the exchange reached the step.</summary>
    public DateTimeOffset Timestamp { get; }

    /// <summary>How long the step took; null while it is running.</summary>
    public TimeSpan? Elapsed
    {
        get
        {
            var ticks = Interlocked.Read(ref _elapsedTicks);
            return ticks < 0 ? null : TimeSpan.FromTicks(ticks);
        }
    }

    /// <inheritdoc />
    public override string ToString() => $"{RouteId}/{NodeId} {Label}";

    /// <summary>Records that the step finished.</summary>
    internal void Done(TimeSpan elapsed) => Interlocked.Exchange(ref _elapsedTicks, Math.Max(0, elapsed.Ticks));
}
