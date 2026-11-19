namespace Caravan.Spi;

/// <summary>
/// Handles an exception that cannot be propagated to a caller, e.g. a failure while a file consumer moves a file after processing (Camel's
/// <c>ExceptionHandler</c>). The default implementations log it.
/// </summary>
public interface IExceptionHandler
{
    /// <summary>Handles <paramref name="exception"/>.</summary>
    /// <param name="message">What failed.</param>
    /// <param name="exchange">The exchange concerned, if any.</param>
    /// <param name="exception">The exception.</param>
    void HandleException(string message, Exchange? exchange, Exception exception);
}
