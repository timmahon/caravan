using System.IO.Compression;
using System.Text;
using Caravan.Spi;

namespace Caravan.DataFormat;

/// <summary>
/// The <c>zipDeflater</c> data format (Camel's <c>zipDeflater</c>): marshal compresses the body's bytes (text as UTF-8) in the zlib (deflate, as Java's <c>Deflater</c>) format;
/// unmarshal decompresses to a byte array.
/// </summary>
public sealed class ZipDeflaterDataFormat : IDataFormat
{
    /// <summary>
    /// The compression level as in Java's <c>Deflater</c>: -1 (default) optimal, 0 none, 1-3 fastest, 4-8 optimal, 9 smallest size.
    /// </summary>
    public int CompressionLevel { get; set; } = -1;

    /// <inheritdoc />
    public async ValueTask MarshalAsync(Exchange exchange, object? graph, Stream output)
    {
        var bytes = await DataFormatHelper.ToBytesAsync(exchange, graph, Encoding.UTF8).ConfigureAwait(false);
        var compressor = new ZLibStream(output, ToLevel(CompressionLevel), leaveOpen: true);
        await using (compressor.ConfigureAwait(false))
        {
            await compressor.WriteAsync(bytes, exchange.CancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async ValueTask<object?> UnmarshalAsync(Exchange exchange, Stream input)
    {
        var decompressor = new ZLibStream(input, CompressionMode.Decompress, leaveOpen: true);
        await using (decompressor.ConfigureAwait(false))
        {
            return await DataFormatHelper.ReadAllAsync(exchange, decompressor).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public override string ToString() => "zipDeflater";

    private static System.IO.Compression.CompressionLevel ToLevel(int level) => level switch
    {
        0 => System.IO.Compression.CompressionLevel.NoCompression,
        >= 1 and <= 3 => System.IO.Compression.CompressionLevel.Fastest,
        9 => System.IO.Compression.CompressionLevel.SmallestSize,
        _ => System.IO.Compression.CompressionLevel.Optimal,
    };
}
