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

    public Exchange(ICaravanContext context, ExchangePattern pattern = ExchangePattern.InOnly)
    {
        Context = context;
        Pattern = pattern;
        ExchangeId = Guid.NewGuid().ToString("N");
        Created = context.TimeProvider.GetUtcNow();
        _message = new Message(this);
    }

    public ICaravanContext Context { get; }

    public string ExchangeId { get; set; }

    public ExchangePattern Pattern { get; set; }

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

    public bool IsFailed => Exception is not null;

    /// <summary>The endpoint that created this exchange (the route's consumer endpoint).</summary>
    public IEndpoint? FromEndpoint { get; set; }

    /// <summary>The id of the route that created this exchange.</summary>
    public string? FromRouteId { get; set; }

    /// <summary>When set, the routing engine stops routing this exchange (the <c>stop</c> EIP).</summary>
    public bool IsRouteStop { get; set; }

    /// <summary>Marks the exchange for rollback without an exception.</summary>
    public bool IsRollbackOnly { get; set; }

    /// <summary>Cancellation for this exchange, typically linked to the context shutdown.</summary>
    public CancellationToken CancellationToken { get; set; }

    /// <summary>Unit-of-work hooks executed when the exchange completes.</summary>
    public IUnitOfWork? UnitOfWork { get; set; }

    public object? GetProperty(string name) => Properties.TryGetValue(name, out var value) ? value : null;

    public T? GetProperty<T>(string name) =>
        Properties.TryGetValue(name, out var value) ? (T?)Context.TypeConverter.Convert(typeof(T), this, value) : default;

    public T GetProperty<T>(string name, T defaultValue) =>
        Properties.TryGetValue(name, out var value) && value is not null
            ? (T)Context.TypeConverter.Convert(typeof(T), this, value)!
            : defaultValue;

    public void SetProperty(string name, object? value) => Properties[name] = value;

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
            IsRouteStop = IsRouteStop,
            IsRollbackOnly = IsRollbackOnly,
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
        copy.ExchangeId = Guid.NewGuid().ToString("N");
        copy.Exception = null;
        copy.IsRouteStop = false;
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
    }

    public override string ToString() => $"Exchange[{ExchangeId}]";
}
