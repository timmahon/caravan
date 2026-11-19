using System.Text;
using Caravan.Spi;
using Caravan.TypeConversion;

namespace Caravan.Processors;

/// <summary>
/// Converts the body to a type (<c>convertBodyTo</c>). A null body is left as is. With <see cref="Mandatory"/>
/// a failed conversion throws <see cref="InvalidPayloadException"/> (caused by
/// <see cref="NoTypeConversionAvailableException"/>); otherwise the body becomes null, as in Camel.
/// </summary>
public sealed class ConvertBodyProcessor(Type type, string? charset = null, bool mandatory = true) : IProcessor
{
    private readonly Encoding? _encoding = charset is null ? null : Encoding.GetEncoding(charset);

    /// <summary>The target type.</summary>
    public Type Type { get; } = type;

    /// <summary>The charset used between text and bytes/streams, e.g. <c>UTF-16</c>; null uses UTF-8.</summary>
    public string? Charset { get; } = charset;

    /// <summary>Whether a failed conversion is an error (default true).</summary>
    public bool Mandatory { get; } = mandatory;

    /// <inheritdoc />
    public ValueTask ProcessAsync(Exchange exchange)
    {
        var message = exchange.Message;
        var body = message.Body;
        if (body is null || Type.IsInstanceOfType(body))
        {
            return ValueTask.CompletedTask;
        }

        if (_encoding is not null && TryConvertWithCharset(body, out var encoded))
        {
            message.Body = encoded;
            return ValueTask.CompletedTask;
        }

        object? value;
        bool converted;
        try
        {
            converted = exchange.Context.TypeConverter.TryConvert(Type, exchange, body, out value);
        }
        catch (Exception e)
        {
            if (Mandatory)
            {
                throw new InvalidPayloadException(exchange, Type, new NoTypeConversionAvailableException(body, Type, e));
            }

            converted = false;
            value = null;
        }

        if (Mandatory && (!converted || value is null))
        {
            throw new InvalidPayloadException(exchange, Type, new NoTypeConversionAvailableException(body, Type));
        }

        message.Body = converted ? value : null;
        return ValueTask.CompletedTask;
    }

    private bool TryConvertWithCharset(object body, out object? result)
    {
        var encoding = _encoding!;
        result = (body, Type) switch
        {
            (byte[] bytes, var t) when t == typeof(string) => encoding.GetString(bytes),
            (Stream stream, var t) when t == typeof(string) => new StreamReader(stream, encoding).ReadToEnd(),
            (string text, var t) when t == typeof(byte[]) => encoding.GetBytes(text),
            (string text, var t) when t == typeof(Stream) => new MemoryStream(encoding.GetBytes(text), writable: false),
            _ => null,
        };
        return result is not null;
    }

    /// <inheritdoc />
    public override string ToString() => $"ConvertBodyTo[{Type.Name}]";
}
