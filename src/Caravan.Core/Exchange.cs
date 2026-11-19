using Caravan.Spi;

namespace Caravan;

/// <summary>
/// A message exchange flowing through a route: the current message, exchange properties,
/// the exchange pattern and any exception that occurred while routing.
/// </summary>
/// <remarks>
/// As in Camel 3+, an exchange carries a single <see cref="Message"/> that processors mutate in place.
/// </remarks>
public sealed class Exchange
{
    private Message _message;
    private List<ISynchronization>? _pendingCompletions;

    /// <summary>Creates an exchange with a new id and an empty message.</summary>
    public Exchange(ICaravanContext context, ExchangePattern pattern = ExchangePattern.InOnly)
    {
        Context = context;
        Pattern = pattern;
        ExchangeId = context.UuidGenerator.GenerateUuid();
        Created = context.TimeProvider.GetUtcNow();
        _message = new Message(this);
    }

    /// <summary>The context the exchange belongs to (used for type conversion, time and logging).</summary>
    public ICaravanContext Context { get; }

    /// <summary>The unique id of the exchange; copies made with <see cref="Copy"/> keep it.</summary>
    public string ExchangeId { get; set; }

    /// <summary>Whether a reply is expected (<c>InOut</c>) or not (<c>InOnly</c>).</summary>
    public ExchangePattern Pattern { get; set; }

    /// <summary>When the exchange was created, from the context's <see cref="TimeProvider"/>.</summary>
    public DateTimeOffset Created { get; }

    /// <summary>The current message.</summary>
    public Message Message
    {
        get => _message;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            value.Exchange = this;
            _message = value;
        }
    }

    /// <summary>Exchange properties: data scoped to this exchange, not sent with the message.</summary>
    public IDictionary<string, object?> Properties { get; private set; } = new Dictionary<string, object?>(StringComparer.Ordinal);

    /// <summary>The exception that occurred while routing, if any.</summary>
    public Exception? Exception { get; set; }

    /// <summary>True when <see cref="Exception"/> is set.</summary>
    public bool IsFailed => Exception is not null;

    /// <summary>The endpoint that created this exchange (the route's consumer endpoint).</summary>
    public IEndpoint? FromEndpoint { get; set; }

    /// <summary>The id of the route that created this exchange.</summary>
    public string? FromRouteId { get; set; }

    /// <summary>
    /// The id of the route currently processing the exchange (Camel's <c>${routeId}</c>). Set when a route
    /// receives the exchange and restored when it returns, e.g. after a <c>direct</c> call.
    /// </summary>
    public string? CurrentRouteId { get; set; }

    /// <summary>When set, the routing engine stops routing this exchange (the <c>stop</c> EIP).</summary>
    public bool IsRouteStop { get; set; }

    /// <summary>Marks the exchange for rollback without an exception.</summary>
    public bool IsRollbackOnly { get; set; }

    /// <summary>
    /// Marks only the current route for rollback (the <c>markRollbackOnlyLast</c> EIP): routing stops in the current route,
    /// and the flag is cleared when the exchange returns to a calling route, which then continues.
    /// </summary>
    public bool IsRollbackOnlyLast { get; set; }

    /// <summary>
    /// Set by an error handler when it handled a failure (an <c>onException</c> with <c>handled(true)</c> or a dead letter
    /// channel): routing stops, but the exchange is not failed. Not copied by <see cref="Copy"/>.
    /// </summary>
    public bool ErrorHandlerHandled { get; set; }

    /// <summary>
    /// Set by an error handler that has exhausted its redeliveries without handling the failure, so error handlers of
    /// enclosing nodes and calling routes let the failure through instead of handling it again. Cleared when the
    /// failure is caught (<c>doCatch</c>). Not copied by <see cref="Copy"/>.
    /// </summary>
    public bool IsRedeliveryExhausted { get; set; }

    /// <summary>
    /// Set on the sub exchanges of a <c>split</c>/<c>multicast</c>/<c>recipientList</c> with <c>shareUnitOfWork</c>: an error
    /// handler that exhausts its redeliveries on such an exchange does not run its failure processor but leaves the failure
    /// to the parent exchange's error handler. Not copied.
    /// </summary>
    internal bool IsSharedUnitOfWorkChild { get; set; }

    /// <summary>
    /// Set on a parent exchange that received the failure of a sub exchange processed with <c>shareUnitOfWork</c>: redelivery
    /// was already attempted for the sub exchange, so the next error handler goes straight to its failure processor. Not copied.
    /// </summary>
    internal bool SuppressRedelivery { get; set; }

    /// <summary>Cancellation for this exchange, typically linked to the context shutdown.</summary>
    public CancellationToken CancellationToken { get; set; }

    /// <summary>Unit-of-work hooks executed when the exchange completes.</summary>
    public IUnitOfWork? UnitOfWork { get; set; }

    /// <summary>
    /// Registers a callback run when the exchange completes: added to the <see cref="UnitOfWork"/> when there is one,
    /// otherwise kept until a unit of work is created for the exchange (e.g. by the route that consumes it).
    /// </summary>
    public void AddOnCompletion(ISynchronization synchronization)
    {
        ArgumentNullException.ThrowIfNull(synchronization);
        if (UnitOfWork is { } unitOfWork)
        {
            unitOfWork.AddSynchronization(synchronization);
            return;
        }

        lock (this)
        {
            (_pendingCompletions ??= []).Add(synchronization);
        }
    }

    /// <summary>Gets a property, or null when it is not set.</summary>
    public object? GetProperty(string name) => Properties.TryGetValue(name, out var value) ? value : null;

    /// <summary>Gets a property converted to <typeparamref name="T"/>, or default when it is not set.</summary>
    public T? GetProperty<T>(string name) =>
        Properties.TryGetValue(name, out var value) ? (T?)Context.TypeConverter.Convert(typeof(T), this, value) : default;

    /// <summary>Gets a property converted to <typeparamref name="T"/>, or <paramref name="defaultValue"/> when it is not set or null.</summary>
    public T GetProperty<T>(string name, T defaultValue) =>
        Properties.TryGetValue(name, out var value) && value is not null
            ? (T)Context.TypeConverter.Convert(typeof(T), this, value)!
            : defaultValue;

    /// <summary>Sets a property, replacing any existing value.</summary>
    public void SetProperty(string name, object? value) => Properties[name] = value;

    /// <summary>Removes a property; returns whether it existed.</summary>
    public bool RemoveProperty(string name) => Properties.Remove(name);

    /// <summary>Removes properties matching the pattern (wildcard <c>*</c> or regex), except the excluded ones.</summary>
    public bool RemoveProperties(string pattern, params string[] excludePatterns)
    {
        var removed = false;
        foreach (var key in Properties.Keys.ToList())
        {
            if (Support.PatternHelper.Matches(key, pattern) &&
                !excludePatterns.Any(p => Support.PatternHelper.Matches(key, p)))
            {
                Properties.Remove(key);
                removed = true;
            }
        }

        return removed;
    }

    /// <summary>Gets the exception of type <typeparamref name="T"/> from the exception or its inner exceptions.</summary>
    public T? GetException<T>()
        where T : Exception
    {
        for (var e = Exception; e is not null; e = e.InnerException)
        {
            if (e is T match)
            {
                return match;
            }

            if (e is AggregateException agg)
            {
                foreach (var inner in agg.InnerExceptions)
                {
                    if (inner is T aggMatch)
                    {
                        return aggMatch;
                    }
                }
            }
        }

        return null;
    }

    /// <summary>Creates a copy of this exchange (same id) with a copied message and properties.</summary>
    public Exchange Copy()
    {
        var copy = new Exchange(Context, Pattern)
        {
            ExchangeId = ExchangeId,
            Exception = Exception,
            FromEndpoint = FromEndpoint,
            FromRouteId = FromRouteId,
            CurrentRouteId = CurrentRouteId,
            IsRouteStop = IsRouteStop,
            IsRollbackOnly = IsRollbackOnly,
            IsRollbackOnlyLast = IsRollbackOnlyLast,
            CancellationToken = CancellationToken,
        };
        copy._message = _message.Copy(copy);
        copy.Properties = new Dictionary<string, object?>(Properties, StringComparer.Ordinal);
        return copy;
    }

    /// <summary>
    /// Creates a copy with a new exchange id, correlated to this one through
    /// <see cref="ExchangePropertyKeys.CorrelationId"/>. Used by EIPs that fan out (split, multicast, wire tap).
    /// </summary>
    public Exchange CreateCorrelatedCopy()
    {
        var copy = Copy();
        copy.ExchangeId = Context.UuidGenerator.GenerateUuid();
        copy.Exception = null;
        copy.IsRouteStop = false;
        copy.IsRollbackOnly = false;
        copy.IsRollbackOnlyLast = false;
        copy.UnitOfWork = null;
        copy.SetProperty(ExchangePropertyKeys.CorrelationId, ExchangeId);
        return copy;
    }

    /// <summary>Copies the message, properties and exception state of <paramref name="source"/> into this exchange.</summary>
    public void CopyResultsFrom(Exchange source)
    {
        if (ReferenceEquals(source, this))
        {
            return;
        }

        _message = source._message.Copy(this);
        Properties = new Dictionary<string, object?>(source.Properties, StringComparer.Ordinal);
        Exception = source.Exception;
        IsRouteStop = source.IsRouteStop;
        IsRollbackOnly = source.IsRollbackOnly;
        IsRollbackOnlyLast = source.IsRollbackOnlyLast;
        ErrorHandlerHandled = source.ErrorHandlerHandled;
        IsRedeliveryExhausted = source.IsRedeliveryExhausted;
    }

    /// <summary>Removes and returns the callbacks registered with <see cref="AddOnCompletion"/> before a unit of work existed.</summary>
    internal List<ISynchronization>? TakePendingCompletions()
    {
        lock (this)
        {
            var pending = _pendingCompletions;
            _pendingCompletions = null;
            return pending;
        }
    }

    /// <summary>Whether callbacks are waiting for a unit of work.</summary>
    internal bool HasPendingCompletions
    {
        get
        {
            lock (this)
            {
                return _pendingCompletions is { Count: > 0 };
            }
        }
    }

    /// <summary>
    /// Copies this exchange (same id) into another context, moving its pending completions along
    /// (used by <c>vm</c> to hand an exchange to a consumer in another context).
    /// </summary>
    internal Exchange CopyToContext(ICaravanContext context)
    {
        if (ReferenceEquals(context, Context))
        {
            return this;
        }

        var copy = new Exchange(context, Pattern)
        {
            ExchangeId = ExchangeId,
            Exception = Exception,
            FromEndpoint = FromEndpoint,
            IsRollbackOnly = IsRollbackOnly,
            CancellationToken = CancellationToken,
        };
        copy._message = _message.Copy(copy);
        copy.Properties = new Dictionary<string, object?>(Properties, StringComparer.Ordinal);
        copy._pendingCompletions = TakePendingCompletions();
        return copy;
    }

    /// <inheritdoc />
    public override string ToString() => $"Exchange[{ExchangeId}]";
}
