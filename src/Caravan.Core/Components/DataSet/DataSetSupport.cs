using Caravan.Components.Mock;
using Caravan.Support;

namespace Caravan.Components.DataSet;

/// <summary>
/// Base class of data sets (Camel's <c>DataSetSupport</c>): the body of message <c>i</c> is <see cref="CreateMessageBody"/>, with the
/// <see cref="DefaultHeaders"/>, then <see cref="OutputTransformer"/> may change the message; received bodies are compared with Camel's type
/// coercion.
/// </summary>
public abstract class DataSetSupport : IDataSet
{
    private long _reportCount;

    /// <inheritdoc />
    public virtual long Size { get; set; } = 10;

    /// <summary>Progress is logged every this many messages (default <see cref="Size"/> / 5).</summary>
    public long ReportCount
    {
        get => _reportCount > 0 ? _reportCount : Math.Max(Size / 5, 1);
        set => _reportCount = value;
    }

    /// <summary>Headers set on every message.</summary>
    public IDictionary<string, object?> DefaultHeaders { get; } = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Changes each produced message after it was filled (Camel's <c>outputTransformer</c>).</summary>
    public Action<Exchange>? OutputTransformer { get; set; }

    /// <inheritdoc />
    public virtual void PopulateMessage(Exchange exchange, long messageIndex)
    {
        ArgumentNullException.ThrowIfNull(exchange);
        var message = exchange.Message;
        message.Body = CreateMessageBody(messageIndex);
        foreach (var (name, value) in DefaultHeaders)
        {
            message.SetHeader(name, value);
        }

        ApplyHeaders(exchange, messageIndex);
        OutputTransformer?.Invoke(exchange);
    }

    /// <inheritdoc />
    public virtual void AssertMessageExpected(DataSetEndpoint endpoint, Exchange expected, Exchange actual, long messageIndex)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(actual);
        var expectedBody = expected.Message.Body;
        var actualBody = actual.Message.Body;
        if (!ObjectHelper.TypeCoerceEquals(actual, actualBody, expectedBody))
        {
            throw new MockAssertionException($"Body of message: {messageIndex}. Expected: <{expectedBody}> but was: <{actualBody}>");
        }
    }

    /// <summary>The body of the message <paramref name="messageIndex"/>.</summary>
    /// <param name="messageIndex">The message number.</param>
    /// <returns>The body.</returns>
    protected abstract object? CreateMessageBody(long messageIndex);

    /// <summary>Sets headers that depend on the message number (none by default).</summary>
    /// <param name="exchange">The exchange.</param>
    /// <param name="messageIndex">The message number.</param>
    protected virtual void ApplyHeaders(Exchange exchange, long messageIndex)
    {
    }
}
