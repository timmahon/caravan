using Caravan.Spi;

namespace Caravan.Processors;

/// <summary>
/// The <c>marshal</c> EIP (Camel's <c>MarshalProcessor</c>): writes the body with the data format and replaces it with the written bytes
/// (a <c>byte[]</c>; Camel produces a stream cache); headers are kept. When the data format has a media type and
/// <see cref="IDataFormatContentType.ContentTypeHeader"/> is on, the <see cref="CaravanHeaders.ContentType"/> header is set.
/// </summary>
public sealed class MarshalProcessor(IDataFormat dataFormat) : IProcessor
{
    /// <summary>The data format.</summary>
    public IDataFormat DataFormat { get; } = dataFormat;

    /// <inheritdoc />
    public async ValueTask ProcessAsync(Exchange exchange)
    {
        var message = exchange.Message;
        using var output = new MemoryStream();
        await DataFormat.MarshalAsync(exchange, message.Body, output).ConfigureAwait(false);
        message.Body = output.ToArray();
        if (DataFormat is IDataFormatContentType { ContentTypeHeader: true, ContentType: { } contentType })
        {
            message.SetHeader(CaravanHeaders.ContentType, contentType);
        }
    }

    /// <inheritdoc />
    public override string ToString() => $"Marshal[{DataFormat}]";
}
