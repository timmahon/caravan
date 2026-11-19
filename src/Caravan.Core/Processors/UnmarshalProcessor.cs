using Caravan.Spi;

namespace Caravan.Processors;

/// <summary>
/// The <c>unmarshal</c> EIP (Camel's <c>UnmarshalProcessor</c>): reads the body, as a stream (text, bytes and streams are accepted), with the
/// data format and replaces it with the result. A null body fails with <see cref="InvalidPayloadException"/> unless
/// <see cref="AllowNullBody"/>. The input stream is disposed afterwards unless the data format returned it.
/// </summary>
public sealed class UnmarshalProcessor(IDataFormat dataFormat, bool allowNullBody = false) : IProcessor
{
    /// <summary>The data format.</summary>
    public IDataFormat DataFormat { get; } = dataFormat;

    /// <summary>Whether a null body is left as is.</summary>
    public bool AllowNullBody { get; } = allowNullBody;

    /// <inheritdoc />
    public async ValueTask ProcessAsync(Exchange exchange)
    {
        var message = exchange.Message;
        var body = message.Body;
        if (body is null)
        {
            if (AllowNullBody)
            {
                return;
            }

            throw new InvalidPayloadException(exchange, typeof(Stream));
        }

        if (body is not Stream input)
        {
            if (!exchange.Context.TypeConverter.TryConvert(typeof(Stream), exchange, body, out var converted) || converted is not Stream stream)
            {
                var text = Support.ObjectHelper.ToText(exchange, body);
                if (text is null)
                {
                    throw new InvalidPayloadException(exchange, typeof(Stream));
                }

                stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(text), writable: false);
            }

            input = stream;
        }
        else if (input is IStreamCache cache)
        {
            cache.Reset();
        }

        object? result = null;
        try
        {
            result = await DataFormat.UnmarshalAsync(exchange, input).ConfigureAwait(false);
        }
        finally
        {
            // A stream cache stays readable after being disposed (it only releases its file handle).
            if (!ReferenceEquals(result, input))
            {
                await input.DisposeAsync().ConfigureAwait(false);
            }
        }

        message.Body = result;
    }

    /// <inheritdoc />
    public override string ToString() => $"Unmarshal[{DataFormat}]";
}
