namespace Caravan.DataTypes;

/// <summary>A message does not conform to a data type (Camel's <c>ValidationException</c>).</summary>
public class ValidationException : CaravanExchangeException
{
    /// <summary>Creates the exception.</summary>
    /// <param name="message">What is wrong.</param>
    /// <param name="exchange">The exchange.</param>
    /// <param name="innerException">The cause, if any.</param>
    public ValidationException(string message, Exchange? exchange, Exception? innerException = null)
        : base(message, exchange, innerException)
    {
    }
}
