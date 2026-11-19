namespace Caravan.Spi;

/// <summary>
/// The unit of work in a route. Processors may throw; the route channel records the exception
/// on <see cref="Exchange.Exception"/> and hands it to the error handler.
/// </summary>
public interface IProcessor
{
    /// <summary>Processes the exchange. May throw; the route records the exception on the exchange.</summary>
    ValueTask ProcessAsync(Exchange exchange);
}
