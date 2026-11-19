using System.Collections.Concurrent;
using System.Globalization;
using Caravan.Model;
using Caravan.Support;

namespace Caravan.Observability;

/// <summary>
/// Camel's <c>BacklogTracer</c>: keeps the last <see cref="BacklogSize"/> traced steps (route inputs and nodes) of the exchanges in memory,
/// for management and debugging tools (<see cref="DumpTracedMessages"/>). Enable it with <see cref="Enabled"/> before routes are created, or
/// keep <see cref="Standby"/> on to enable it at runtime.
/// </summary>
public sealed class BacklogTracer
{
    private readonly ConcurrentQueue<BacklogTracerEventMessage> _queue = new();
    private long _uid;
    private long _traceCounter;
    private int _count;

    /// <summary>Whether steps are recorded.</summary>
    public bool Enabled { get; set; }

    /// <summary>Whether routes are prepared for backlog tracing even when it is disabled when they are created.</summary>
    public bool Standby { get; set; }

    /// <summary>The maximum number of steps kept (default 100); the oldest are dropped.</summary>
    public int BacklogSize
    {
        get;
        set => field = value > 0 ? value : throw new ArgumentOutOfRangeException(nameof(value), "Must be positive");
    } = 100;

    /// <summary>Whether the dumped steps are removed from the backlog (default true, as Camel).</summary>
    public bool RemoveOnDump { get; set; } = true;

    /// <summary>The maximum length of the body text (default 32768).</summary>
    public int BodyMaxChars { get; set; } = 32 * 1024;

    /// <summary>
    /// Comma-separated patterns (exact, wildcard or regular expression) of route ids or node ids to include; null includes everything.
    /// </summary>
    public string? IncludePatterns { get; set; }

    /// <summary>Comma-separated patterns of route ids or node ids to exclude (wins over <see cref="IncludePatterns"/>).</summary>
    public string? ExcludePatterns { get; set; }

    /// <summary>Only exchanges matching this predicate are traced (Camel's <c>traceFilter</c>); null traces every exchange.</summary>
    public IPredicate? TraceFilter { get; set; }

    /// <summary>Whether the route inputs are recorded too (default true).</summary>
    public bool TraceRouteInput { get; set; } = true;

    /// <summary>The number of steps recorded since the last <see cref="ResetTraceCounter"/>.</summary>
    public long TraceCounter => Interlocked.Read(ref _traceCounter);

    /// <summary>The number of steps currently kept.</summary>
    public int Count => Volatile.Read(ref _count);

    /// <summary>Whether routes created now need the tracing wrapper.</summary>
    internal bool Installed => Enabled || Standby;

    /// <summary>The kept steps of a node (or, with null, all of them), oldest first; removed when <see cref="RemoveOnDump"/>.</summary>
    /// <param name="nodeId">A node id or route id; null for every step.</param>
    /// <returns>The steps.</returns>
    public IReadOnlyList<BacklogTracerEventMessage> DumpTracedMessages(string? nodeId = null)
    {
        var result = _queue.Where(e => nodeId is null || e.NodeId == nodeId || (e.IsRouteInput && e.RouteId == nodeId)).ToList();
        if (RemoveOnDump)
        {
            Remove(result);
        }

        return result;
    }

    /// <summary>All kept steps, oldest first; removed when <see cref="RemoveOnDump"/>.</summary>
    /// <returns>The steps.</returns>
    public IReadOnlyList<BacklogTracerEventMessage> DumpAllTracedMessages() => DumpTracedMessages();

    /// <summary>The kept steps as text, one per line.</summary>
    /// <param name="nodeId">A node id or route id; null for every step.</param>
    /// <returns>The text.</returns>
    public string DumpTracedMessagesAsText(string? nodeId = null) =>
        string.Join(
            Environment.NewLine,
            DumpTracedMessages(nodeId).Select(e => string.Create(
                CultureInfo.InvariantCulture,
                $"{e.Uid} {e.Timestamp:O} [{e.RouteId}] [{e.NodeId ?? "from"}] {e.Label} {e.ExchangeId} ({e.Elapsed?.TotalMilliseconds} ms) body: {e.Body}")));

    /// <summary>Removes every kept step.</summary>
    public void Clear()
    {
        while (_queue.TryDequeue(out _))
        {
            Interlocked.Decrement(ref _count);
        }
    }

    /// <summary>Resets <see cref="TraceCounter"/>.</summary>
    public void ResetTraceCounter() => Interlocked.Exchange(ref _traceCounter, 0);

    /// <summary>Records a step when the tracer is enabled and the step and exchange pass the filters.</summary>
    internal BacklogTracerEventMessage? Trace(Exchange exchange, string routeId, ProcessorDefinition? definition)
    {
        if (!Enabled || (definition is null && !TraceRouteInput) || !Included(routeId, definition?.Id))
        {
            return null;
        }

        if (TraceFilter is { } filter && !filter.Matches(exchange))
        {
            return null;
        }

        var message = exchange.Message;
        var body = message.Body;
        string? bodyText;
        try
        {
            bodyText = body switch
            {
                null => null,
                string text => text,
                Stream => "[Body is instance of " + body.GetType().Name + "]",
                _ => exchange.Context.TypeConverter.TryConvert(typeof(string), exchange, body, out var text) ? text as string : body.ToString(),
            };
        }

        catch (Exception e)
        {
            bodyText = "[Body cannot be converted: " + e.Message + "]";
        }

        if (bodyText is not null && bodyText.Length > BodyMaxChars)
        {
            bodyText = bodyText[..BodyMaxChars] + "... [Body clipped after " + BodyMaxChars + " chars, total length is " + bodyText.Length + "]";
        }

        var headers = message.Headers.ToDictionary(h => h.Key, h => h.Value?.ToString(), StringComparer.OrdinalIgnoreCase);
        var entry = new BacklogTracerEventMessage(
            Interlocked.Increment(ref _uid),
            exchange.Context.TimeProvider.GetUtcNow(),
            routeId,
            definition?.Id,
            definition?.Label,
            exchange.ExchangeId,
            bodyText,
            body?.GetType().FullName,
            headers);
        _queue.Enqueue(entry);
        Interlocked.Increment(ref _traceCounter);
        if (Interlocked.Increment(ref _count) > BacklogSize && _queue.TryDequeue(out _))
        {
            Interlocked.Decrement(ref _count);
        }

        return entry;
    }

    private void Remove(List<BacklogTracerEventMessage> entries)
    {
        if (entries.Count == 0)
        {
            return;
        }

        var remove = new HashSet<BacklogTracerEventMessage>(entries, ReferenceEqualityComparer.Instance);
        var keep = new List<BacklogTracerEventMessage>();
        lock (_queue)
        {
            var n = _queue.Count;
            for (var i = 0; i < n && _queue.TryDequeue(out var e); i++)
            {
                Interlocked.Decrement(ref _count);
                if (!remove.Contains(e))
                {
                    keep.Add(e);
                }
            }

            foreach (var e in keep)
            {
                _queue.Enqueue(e);
                Interlocked.Increment(ref _count);
            }
        }
    }

    private bool Included(string routeId, string? nodeId)
    {
        if (ExcludePatterns is { } exclude && Matches(exclude))
        {
            return false;
        }

        return IncludePatterns is not { } include || Matches(include);

        bool Matches(string patterns)
        {
            foreach (var pattern in patterns.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (PatternHelper.Matches(routeId, pattern) || (nodeId is not null && PatternHelper.Matches(nodeId, pattern)))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
