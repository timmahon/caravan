namespace Caravan.DataTypes;

/// <summary>
/// Transforms by calling an endpoint (Camel's <c>endpointTransformer</c>): a copy of the exchange is sent <c>InOut</c> to <see cref="Uri"/>
/// (e.g. a <c>direct:</c> route doing the mapping) and the message takes the reply's body and headers. A failed call fails the transformation.
/// </summary>
/// <param name="from">The source type.</param>
/// <param name="to">The target type.</param>
/// <param name="uri">The endpoint URI.</param>
public sealed class EndpointTransformer(DataType? from, DataType? to, string uri) : ITransformer
{
    /// <inheritdoc />
    public DataType? From { get; } = from;

    /// <inheritdoc />
    public DataType? To { get; } = to;

    /// <inheritdoc />
    public string? Name { get; init; }

    /// <summary>The endpoint URI.</summary>
    public string Uri { get; } = uri;

    /// <inheritdoc />
    public async ValueTask TransformAsync(Message message, DataType? from, DataType to)
    {
        var exchange = message.Exchange;
        var copy = exchange.Copy();
        copy.Pattern = ExchangePattern.InOut;
        copy.UnitOfWork = exchange.UnitOfWork;
        var result = await exchange.Context.CreateProducerTemplate().SendAsync(Uri, copy).ConfigureAwait(false);
        if (result.Exception is { } failure)
        {
            throw new CaravanExchangeException($"Transformer endpoint {Uri} failed", exchange, failure);
        }

        message.Body = result.Message.Body;
        message.SetHeaders(result.Message.Headers);
    }

    /// <inheritdoc />
    public override string ToString() => $"EndpointTransformer[{From} -> {To}, {Uri}]";
}
