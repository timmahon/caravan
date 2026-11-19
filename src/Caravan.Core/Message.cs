namespace Caravan;

/// <summary>
/// A message: a body plus headers. Header names are case-insensitive, as in Camel.
/// </summary>
public sealed class Message
{
    private object? _body;

    public Message(Exchange exchange)
    {
        Exchange = exchange;
        MessageId = Guid.NewGuid().ToString("N");
    }

    /// <summary>The exchange this message belongs to.</summary>
    public Exchange Exchange { get; internal set; }

    public string MessageId { get; set; }

    public IDictionary<string, object?> Headers { get; private set; } =
        new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

    public object? Body
    {
        get => _body;
        set => _body = value;
    }

    /// <summary>Gets the body converted to <typeparamref name="T"/> using the context's type converters.</summary>
    /// <exception cref="TypeConversion.NoTypeConversionAvailableException">If the body cannot be converted.</exception>
    public T? GetBody<T>() => (T?)GetBody(typeof(T));

    public object? GetBody(Type type) => Exchange.Context.TypeConverter.Convert(type, Exchange, _body);

    /// <summary>Gets the body converted to <typeparamref name="T"/>, throwing if it is null.</summary>
    public T GetMandatoryBody<T>() =>
        GetBody<T>() ?? throw new InvalidPayloadException(Exchange, typeof(T));

    public bool TryGetBody<T>(out T? value)
    {
        if (Exchange.Context.TypeConverter.TryConvert(typeof(T), Exchange, _body, out var result))
        {
            value = (T?)result;
            return true;
        }

        value = default;
        return false;
    }

    public object? GetHeader(string name) => Headers.TryGetValue(name, out var value) ? value : null;

    public T? GetHeader<T>(string name) => (T?)GetHeader(name, typeof(T));

    public object? GetHeader(string name, Type type) =>
        Headers.TryGetValue(name, out var value) ? Exchange.Context.TypeConverter.Convert(type, Exchange, value) : null;

    public T GetHeader<T>(string name, T defaultValue) =>
        Headers.TryGetValue(name, out var value) && value is not null
            ? (T)Exchange.Context.TypeConverter.Convert(typeof(T), Exchange, value)!
            : defaultValue;

    public bool HasHeader(string name) => Headers.ContainsKey(name);

    public void SetHeader(string name, object? value) => Headers[name] = value;

    public bool RemoveHeader(string name) => Headers.Remove(name);

    /// <summary>Removes headers whose name matches the pattern (wildcard <c>*</c> or regex), except the excluded ones.</summary>
    public bool RemoveHeaders(string pattern, params string[] excludePatterns)
    {
        var removed = false;
        foreach (var key in Headers.Keys.ToList())
        {
            if (Support.PatternHelper.Matches(key, pattern) &&
                !excludePatterns.Any(p => Support.PatternHelper.Matches(key, p)))
            {
                Headers.Remove(key);
                removed = true;
            }
        }

        return removed;
    }

    public void SetHeaders(IDictionary<string, object?> headers)
    {
        Headers = new Dictionary<string, object?>(headers, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Copies the body, headers and message id from another message.</summary>
    public void CopyFrom(Message other)
    {
        MessageId = other.MessageId;
        _body = other._body;
        Headers = new Dictionary<string, object?>(other.Headers, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Creates a copy bound to <paramref name="exchange"/>.</summary>
    public Message Copy(Exchange exchange)
    {
        var copy = new Message(exchange);
        copy.CopyFrom(this);
        return copy;
    }

    public override string ToString() => $"Message[{MessageId}]";
}
