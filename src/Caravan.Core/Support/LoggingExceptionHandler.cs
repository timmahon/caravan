using Caravan.Spi;
using Microsoft.Extensions.Logging;

namespace Caravan.Support;

/// <summary>An <see cref="IExceptionHandler"/> that logs exceptions as warnings (Camel's <c>LoggingExceptionHandler</c>).</summary>
/// <param name="logger">The logger.</param>
public sealed class LoggingExceptionHandler(ILogger logger) : IExceptionHandler
{
    /// <inheritdoc />
    public void HandleException(string message, Exchange? exchange, Exception exception) =>
        logger.LogWarning(exception, "{Message}{Exchange}", message, exchange is null ? string.Empty : " on " + exchange);
}
