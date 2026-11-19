using Caravan.Spi;
using Caravan.TypeConversion;

namespace Caravan.Processors;

/// <summary>
/// Converts a variable to a type (<c>convertVariableTo</c>), optionally storing it under another name. A missing variable is left as is.
/// With <see cref="Mandatory"/> a failed conversion throws <see cref="NoTypeConversionAvailableException"/>; otherwise the variable is removed.
/// </summary>
public sealed class ConvertVariableProcessor(string name, Type type, string? toName = null, bool mandatory = true) : IProcessor
{
    /// <summary>The variable to convert.</summary>
    public string Name { get; } = name;

    /// <summary>The target type.</summary>
    public Type Type { get; } = type;

    /// <summary>The variable receiving the converted value; null replaces <see cref="Name"/>.</summary>
    public string? ToName { get; } = toName;

    /// <summary>Whether a failed conversion is an error (default true).</summary>
    public bool Mandatory { get; } = mandatory;

    /// <inheritdoc />
    public ValueTask ProcessAsync(Exchange exchange)
    {
        var value = exchange.GetVariable(Name);
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

        exchange.SetVariable(ToName ?? Name, result);
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public override string ToString() => $"ConvertVariableTo[{Name}, {Type.Name}]";
}
