namespace Caravan.TypeConversion;

/// <summary>Converts a value to another type. Return <c>false</c> when the conversion is not supported.</summary>
public delegate bool TypeConverterFunc(object value, Exchange? exchange, out object? result);
