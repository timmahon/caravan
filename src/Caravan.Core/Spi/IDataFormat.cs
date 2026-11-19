namespace Caravan.Spi;

/// <summary>
/// A data format (Camel's <c>DataFormat</c>): turns an object graph into bytes (<c>marshal</c>) and bytes back into objects
/// (<c>unmarshal</c>). Used by the <c>marshal</c>/<c>unmarshal</c> EIPs and the <c>dataformat</c> component; resolved by name with
/// <see cref="ICaravanContext.ResolveDataFormat"/>. Implementations must be thread-safe once configured: one instance serves all exchanges.
/// A data format that is an <see cref="IService"/> is started and stopped with the route using it.
/// </summary>
public interface IDataFormat
{
    /// <summary>Writes <paramref name="graph"/> (usually the message body) to <paramref name="output"/>.</summary>
    /// <param name="exchange">The exchange being processed (for type conversion and headers).</param>
    /// <param name="graph">The object to marshal.</param>
    /// <param name="output">The stream to write to; do not close it.</param>
    /// <returns>A task completed when the graph has been written.</returns>
    ValueTask MarshalAsync(Exchange exchange, object? graph, Stream output);

    /// <summary>Reads an object from <paramref name="input"/>.</summary>
    /// <param name="exchange">The exchange being processed.</param>
    /// <param name="input">The stream to read (the message body as a stream).</param>
    /// <returns>The unmarshalled object, which becomes the message body.</returns>
    ValueTask<object?> UnmarshalAsync(Exchange exchange, Stream input);
}
