namespace Caravan;

/// <summary>The body was missing or could not be converted to the required type.</summary>
/// <param name="exchange">The exchange whose body was required.</param>
/// <param name="type">The required body type.</param>
/// <param name="innerException">The cause, e.g. a <see cref="TypeConversion.NoTypeConversionAvailableException"/>.</param>
public class InvalidPayloadException(Exchange exchange, Type type, Exception? innerException = null)
    : CaravanExchangeException($"No body available of type: {type.FullName}", exchange, innerException)
{
    /// <summary>The required body type.</summary>
    public Type Type { get; } = type;
}
