namespace Caravan;

/// <summary>The body was missing or could not be converted to the required type.</summary>
public class InvalidPayloadException(Exchange exchange, Type type)
    : CaravanExchangeException($"No body available of type: {type.FullName}", exchange)
{
    /// <summary>The required body type.</summary>
    public Type Type { get; } = type;
}
