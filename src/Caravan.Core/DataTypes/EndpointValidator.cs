namespace Caravan.DataTypes;

/// <summary>
/// Validates by calling an endpoint (Camel's <c>endpointValidator</c>): a copy of the exchange is sent to <see cref="Uri"/>; the message is invalid
/// when the call fails.
/// </summary>
/// <param name="type">The data type validated.</param>
/// <param name="uri">The endpoint URI.</param>
public sealed class EndpointValidator(DataType type, string uri) : IValidator
{
    /// <inheritdoc />
    public DataType Type { get; } = type;

    /// <summary>The endpoint URI.</summary>
    public string Uri { get; } = uri;

    /// <inheritdoc />
    public async ValueTask ValidateAsync(Message message, DataType type)
    {
        var exchange = message.Exchange;
        var copy = exchange.Copy();
        copy.UnitOfWork = exchange.UnitOfWork;
        var result = await exchange.Context.CreateProducerTemplate().SendAsync(Uri, copy).ConfigureAwait(false);
        if (result.Exception is { } failure)
        {
            throw new ValidationException($"Validation of {type} by {Uri} failed: {failure.Message}", exchange, failure);
        }
    }

    /// <inheritdoc />
    public override string ToString() => $"EndpointValidator[{Type}, {Uri}]";
}
