using Caravan.Spi;
using Caravan.Support;

namespace Caravan.DataFormat;

/// <summary>
/// The <c>string</c> data format (Camel's <c>string</c>): marshal writes the body as text in <see cref="Charset"/>; unmarshal reads the input
/// as a string.
/// </summary>
public sealed class StringDataFormat : IDataFormat
{
    /// <summary>Creates the data format (UTF-8).</summary>
    public StringDataFormat()
    {
    }

    /// <summary>Creates the data format with a charset.</summary>
    /// <param name="charset">The charset, e.g. <c>ISO-8859-1</c>.</param>
    public StringDataFormat(string? charset)
    {
        Charset = charset;
    }

    /// <summary>The charset (default UTF-8).</summary>
    public string? Charset { get; set; }

    /// <inheritdoc />
    public async ValueTask MarshalAsync(Exchange exchange, object? graph, Stream output)
    {
        var encoding = DataFormatHelper.GetEncoding(Charset);
        var bytes = graph switch
        {
            null => [],
            byte[] raw => raw,
            Stream => await DataFormatHelper.ToBytesAsync(exchange, graph, encoding).ConfigureAwait(false),
            _ => encoding.GetBytes(ObjectHelper.ToText(exchange, graph) ?? string.Empty),
        };
        await output.WriteAsync(bytes, exchange.CancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<object?> UnmarshalAsync(Exchange exchange, Stream input)
    {
        using var reader = new StreamReader(input, DataFormatHelper.GetEncoding(Charset), detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        return await reader.ReadToEndAsync(exchange.CancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public override string ToString() => Charset is null ? "string" : $"string[{Charset}]";
}
