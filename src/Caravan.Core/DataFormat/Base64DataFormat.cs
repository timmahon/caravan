using System.Text;
using Caravan.Spi;

namespace Caravan.DataFormat;

/// <summary>
/// The <c>base64</c> data format (Camel's <c>camel-base64</c>). Marshal encodes the body's bytes (text as UTF-8) as base64 text (ASCII bytes),
/// broken into lines of <see cref="LineLength"/> characters, each ended by <see cref="LineSeparator"/> (as commons-codec's chunked output; 0 =
/// one line without separator); <see cref="UrlSafe"/> uses the URL-safe alphabet without padding. Unmarshal decodes either alphabet, ignoring
/// whitespace and missing padding, to a byte array.
/// </summary>
public sealed class Base64DataFormat : IDataFormat
{
    /// <summary>The maximum line length of encoded output (default 76, as Camel); 0 or less for no line breaks.</summary>
    public int LineLength { get; set; } = 76;

    /// <summary>The line separator of encoded output (default CRLF).</summary>
    public string LineSeparator { get; set; } = "\r\n";

    /// <summary>Use the URL-safe alphabet (<c>-</c> and <c>_</c>) without padding (default false).</summary>
    public bool UrlSafe { get; set; }

    /// <summary>Encodes bytes as configured.</summary>
    /// <param name="data">The bytes.</param>
    /// <returns>The base64 text.</returns>
    public string Encode(byte[] data)
    {
        var text = Convert.ToBase64String(data);
        if (UrlSafe)
        {
            text = text.TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        if (LineLength <= 0 || text.Length == 0)
        {
            return text;
        }

        var builder = new StringBuilder(text.Length + ((text.Length / LineLength) + 1) * LineSeparator.Length);
        for (var i = 0; i < text.Length; i += LineLength)
        {
            builder.Append(text, i, Math.Min(LineLength, text.Length - i)).Append(LineSeparator);
        }

        return builder.ToString();
    }

    /// <summary>Decodes base64 text in either alphabet, ignoring whitespace and missing padding.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The bytes.</returns>
    /// <exception cref="FormatException">The text is not base64.</exception>
    public static byte[] Decode(string text)
    {
        var builder = new StringBuilder(text.Length + 3);
        foreach (var c in text)
        {
            if (!char.IsWhiteSpace(c))
            {
                builder.Append(c switch { '-' => '+', '_' => '/', _ => c });
            }
        }

        while (builder.Length % 4 != 0)
        {
            builder.Append('=');
        }

        return Convert.FromBase64String(builder.ToString());
    }

    /// <inheritdoc />
    public async ValueTask MarshalAsync(Exchange exchange, object? graph, Stream output)
    {
        var bytes = await DataFormatHelper.ToBytesAsync(exchange, graph, Encoding.UTF8).ConfigureAwait(false);
        await output.WriteAsync(Encoding.ASCII.GetBytes(Encode(bytes)), exchange.CancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<object?> UnmarshalAsync(Exchange exchange, Stream input)
    {
        var bytes = await DataFormatHelper.ReadAllAsync(exchange, input).ConfigureAwait(false);
        return Decode(Encoding.ASCII.GetString(bytes));
    }

    /// <inheritdoc />
    public override string ToString() => "base64";
}
