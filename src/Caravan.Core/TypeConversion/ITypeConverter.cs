namespace Caravan.TypeConversion;

/// <summary>Converts between types, like Camel's <c>TypeConverter</c>.</summary>
public interface ITypeConverter
{
    /// <summary>Converts <paramref name="value"/>; returns null for null. Throws <see cref="NoTypeConversionAvailableException"/> if not possible.</summary>
    object? Convert(Type toType, Exchange? exchange, object? value);

    /// <summary>Tries to convert <paramref name="value"/>; returns false when it cannot be converted. Null converts to null for reference and nullable types.</summary>
    bool TryConvert(Type toType, Exchange? exchange, object? value, out object? result);
}
