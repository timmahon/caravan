using Caravan.Spi;
using Caravan.TypeConversion;

namespace Caravan.Processors;

/// <summary>
/// Converts a header to a type (<c>convertHeaderTo</c>), optionally storing it under another name. A missing or
/// null header is left as is. With <see cref="Mandatory"/> a failed conversion throws
/// <see cref="NoTypeConversionAvailableException"/>; otherwise the header becomes null.
/// </summary>
public sealed class ConvertHeaderProcessor(string name, Type type, string? toName = null, bool mandatory = true) : IProcessor
{
    /// <summary>The header to convert.</summary>
    public string Name { get; } = name;

    /// <summary>The target type.</summary>
    public Type Type { get; } = type;

    /// <summary>The header receiving the converted value; null replaces <see cref="Name"/>.</summary>
    public string? ToName { get; } = toName;

    /// <summary>Whether a failed conversion is an error (default true).</summary>
    public bool Mandatory { get; } = mandatory;

    /// <inheritdoc />
    public ValueTask ProcessAsync(Exchange exchange)
    {
        var value = exchange.Message.GetHeader(Name);
        if (value is null)
        {
            return ValueTask.CompletedTask;
        }

        var converter = exchange.Context.TypeConverter;
        object? result;
        if (Mandatory)
        {
            result = converter.Convert(Type, exchange, value) ?? throw new NoTypeConversionAvailableException(value, Type);
        }
        else
        {
            result = converter.TryConvert(Type, exchange, value, out var converted) ? converted : null;
        }

        exchange.Message.SetHeader(ToName ?? Name, result);
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public override string ToString() => $"ConvertHeaderTo[{Name}, {Type.Name}]";
}
