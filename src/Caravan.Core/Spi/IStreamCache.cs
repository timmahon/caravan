namespace Caravan.Spi;

/// <summary>
/// A re-readable cached stream (Camel's <c>StreamCache</c>): stream caching turns stream bodies into one, so that several nodes can read
/// the body (each node sees it from the start, see <see cref="Streams.StreamCachingStrategy"/>). Implementations are <see cref="Stream"/>s that can
/// be read, sought and reset; disposing one only releases resources that are acquired again when needed, so the data stays readable until
/// the exchange that created the cache completes.
/// </summary>
public interface IStreamCache
{
    /// <summary>The number of cached bytes.</summary>
    long Length { get; }

    /// <summary>Whether the data is held in memory (false when it was spooled to a temporary file).</summary>
    bool IsInMemory { get; }

    /// <summary>Moves back to the start of the data, so the next read sees all of it.</summary>
    void Reset();

    /// <summary>
    /// Creates an independent reader over the same data, positioned at the start (used when the exchange is copied, e.g. for each branch of a
    /// <c>multicast</c>), so readers do not share a position.
    /// </summary>
    /// <returns>The copy.</returns>
    IStreamCache Copy();

    /// <summary>Writes all the data to <paramref name="output"/> (from the start, whatever the current position) and resets the cache.</summary>
    /// <param name="output">The stream to write to.</param>
    /// <param name="cancellationToken">Cancels writing.</param>
    /// <returns>A task completed when the data has been written.</returns>
    Task WriteToAsync(Stream output, CancellationToken cancellationToken = default);
}
