namespace Caravan.Spi;

/// <summary>
/// Decides which headers cross the boundary between a message and a transport (Camel's <c>HeaderFilterStrategy</c>), e.g. which message
/// headers become HTTP response headers and which HTTP request headers become message headers.
/// </summary>
public interface IHeaderFilterStrategy
{
    /// <summary>Whether a message header is kept out of the transport (outbound: message to transport).</summary>
    /// <param name="name">The header name.</param>
    /// <param name="value">The value.</param>
    /// <param name="exchange">The exchange.</param>
    /// <returns>True to filter the header out.</returns>
    bool ApplyFilterToCaravanHeaders(string name, object? value, Exchange exchange);

    /// <summary>Whether a transport header is kept out of the message (inbound: transport to message).</summary>
    /// <param name="name">The header name.</param>
    /// <param name="value">The value.</param>
    /// <param name="exchange">The exchange.</param>
    /// <returns>True to filter the header out.</returns>
    bool ApplyFilterToExternalHeaders(string name, object? value, Exchange exchange);
}
