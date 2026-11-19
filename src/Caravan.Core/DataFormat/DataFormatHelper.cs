using System.Text;

namespace Caravan.DataFormat;

/// <summary>Conversions shared by the built-in data formats.</summary>
internal static class DataFormatHelper
{
    /// <summary>The graph as bytes: byte arrays as is, streams read, text encoded with <paramref name="encoding"/>, anything else converted.</summary>
    public static async ValueTask<byte[]> ToBytesAsync(Exchange exchange, object? graph, Encoding encoding)
    {
        switch (graph)
        {
            case null:
                return [];
            case byte[] bytes:
                return bytes;
            case string text:
                return encoding.GetBytes(text);
            case Stream stream:
                if (stream is Spi.IStreamCache cache)
                {
                    cache.Reset();
                }

                using (var buffer = new MemoryStream())
                {
                    await stream.CopyToAsync(buffer, exchange.CancellationToken).ConfigureAwait(false);
                    return buffer.ToArray();
                }

            default:
                return (byte[])exchange.Context.TypeConverter.Convert(typeof(byte[]), exchange, graph)!;
        }
    }

    /// <summary>Reads the whole input.</summary>
    public static async ValueTask<byte[]> ReadAllAsync(Exchange exchange, Stream input)
    {
        using var buffer = new MemoryStream();
        await input.CopyToAsync(buffer, exchange.CancellationToken).ConfigureAwait(false);
        return buffer.ToArray();
    }

    /// <summary>The encoding named <paramref name="charset"/>, without byte order mark for UTF-8 (default).</summary>
    public static Encoding GetEncoding(string? charset) =>
        charset is null || charset.Equals("UTF-8", StringComparison.OrdinalIgnoreCase) || charset.Equals("UTF8", StringComparison.OrdinalIgnoreCase)
            ? new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)
            : Encoding.GetEncoding(charset);
}
