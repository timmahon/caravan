namespace Caravan.TypeConversion;

/// <summary>No conversion exists from a value to a type.</summary>
public class NoTypeConversionAvailableException(object? value, Type toType, Exception? inner = null)
    : CaravanException($"No type converter available to convert from type: {value?.GetType().FullName ?? "null"} to the required type: {toType.FullName}", inner)
{
    /// <summary>The value that could not be converted.</summary>
    public object? Value { get; } = value;

    /// <summary>The requested type.</summary>
    public Type ToType { get; } = toType;
}
