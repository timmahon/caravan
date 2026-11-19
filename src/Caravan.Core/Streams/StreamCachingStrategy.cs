using Caravan.Spi;

namespace Caravan.Streams;

/// <summary>
/// Stream caching (Camel's <c>StreamCachingStrategy</c>), <see cref="ICaravanContext.StreamCaching"/>. When enabled for a route
/// (<see cref="Enabled"/>, overridden per route with <c>.StreamCaching(bool)</c>), a body that is a <see cref="Stream"/> is read once
/// into an <see cref="IStreamCache"/> before the next node runs, and a stream cache body is reset before every node, so that every node
/// (and every branch of a fan-out, which gets its own reader) reads the whole body. Data is kept in memory, or, with
/// <see cref="SpoolEnabled"/>, spooled to a temporary file in <see cref="SpoolDirectory"/> once it exceeds <see cref="SpoolThreshold"/>; the
/// file is deleted when the exchange completes, the directory when the context stops (<see cref="RemoveSpoolDirectoryWhenStopping"/>).
/// </summary>
/// <remarks>
/// As in Camel 4 stream caching is enabled by default and spooling to disk is disabled by default. Unlike Camel any stream is cached,
/// seekable or not (a seekable stream would otherwise still be left at its end by the first reader). Configure it before the routes are
/// created.
/// </remarks>
public sealed class StreamCachingStrategy
{
    /// <summary>The default <see cref="SpoolThreshold"/>: 128 KB, as in Camel.</summary>
    public const long DefaultSpoolThreshold = 128 * 1024;

    private readonly Lock _lock = new();
    private string? _spoolDirectory;
    private bool _directoryCreated;
    private long _memoryCounter;
    private long _memorySize;
    private long _spoolCounter;
    private long _spoolSize;
    private long _spoolDeleted;

    /// <summary>Whether routes cache stream bodies by default (default true, as in Camel 4).</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Whether large streams are spooled to temporary files (default false, as in Camel 4).</summary>
    public bool SpoolEnabled { get; set; }

    /// <summary>The size in bytes above which data is spooled to a file when <see cref="SpoolEnabled"/> (default 128 KB).</summary>
    public long SpoolThreshold { get; set; } = DefaultSpoolThreshold;

    /// <summary>
    /// The directory of temporary files; default <c>&lt;temp&gt;/caravan/caravan-tmp-&lt;uuid&gt;</c> (one per strategy, created on first use).
    /// </summary>
    public string SpoolDirectory
    {
        get
        {
            lock (_lock)
            {
                return _spoolDirectory ??= Path.Combine(Path.GetTempPath(), "caravan", "caravan-tmp-" + Guid.NewGuid().ToString("N"));
            }
        }

        set
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value);
            lock (_lock)
            {
                _spoolDirectory = value;
                _directoryCreated = false;
            }
        }
    }

    /// <summary>The buffer size used when copying streams and writing spool files (default 4096).</summary>
    public int BufferSize { get; set; } = 4096;

    /// <summary>Whether the spool directory is deleted when the context stops (default true).</summary>
    public bool RemoveSpoolDirectoryWhenStopping { get; set; } = true;

    /// <summary>The number of streams cached in memory.</summary>
    public long CacheMemoryCounter => Interlocked.Read(ref _memoryCounter);

    /// <summary>The total number of bytes cached in memory.</summary>
    public long CacheMemorySize => Interlocked.Read(ref _memorySize);

    /// <summary>The number of streams spooled to temporary files.</summary>
    public long CacheSpoolCounter => Interlocked.Read(ref _spoolCounter);

    /// <summary>The total number of bytes spooled to temporary files.</summary>
    public long CacheSpoolSize => Interlocked.Read(ref _spoolSize);

    /// <summary>The number of temporary files deleted so far.</summary>
    public long SpoolFilesDeleted => Interlocked.Read(ref _spoolDeleted);

    /// <summary>
    /// Prepares the body of <paramref name="exchange"/> for the next reader: a stream cache is reset; another <see cref="Stream"/> is read
    /// into a stream cache (and disposed) which replaces the body. Other bodies are left alone.
    /// </summary>
    /// <param name="exchange">The exchange.</param>
    /// <returns>The stream cache now in the body, or null when the body is not a stream.</returns>
    public async ValueTask<IStreamCache?> CacheAsync(Exchange exchange)
    {
        ArgumentNullException.ThrowIfNull(exchange);
        switch (exchange.Message.Body)
        {
            case IStreamCache cache:
                cache.Reset();
                return cache;
            case Stream stream:
                var cached = await CacheAsync(exchange, stream).ConfigureAwait(false);
                exchange.Message.Body = cached;
                return cached;
            default:
                return null;
        }
    }

    /// <summary>Reads <paramref name="stream"/> into a new stream cache owned by <paramref name="exchange"/>, and disposes it.</summary>
    /// <param name="exchange">The exchange whose completion deletes a spooled file.</param>
    /// <param name="stream">The stream to read (from its current position).</param>
    /// <returns>The cache.</returns>
    public async ValueTask<IStreamCache> CacheAsync(Exchange exchange, Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        await using (stream.ConfigureAwait(false))
        {
            var output = new CachedOutputStream(this, exchange);
            try
            {
                await stream.CopyToAsync(output, BufferSize, exchange.CancellationToken).ConfigureAwait(false);
                return output.NewStreamCache();
            }
            catch
            {
                await output.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }
    }

    /// <summary>Deletes the spool directory (when <see cref="RemoveSpoolDirectoryWhenStopping"/>); called when the context stops.</summary>
    public void Stop()
    {
        string? directory;
        lock (_lock)
        {
            directory = _directoryCreated && RemoveSpoolDirectoryWhenStopping ? _spoolDirectory : null;
            _directoryCreated = false;
        }

        if (directory is not null && Directory.Exists(directory))
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // best effort
            }
        }
    }

    /// <inheritdoc />
    public override string ToString() =>
        $"StreamCachingStrategy[enabled={Enabled}, spoolEnabled={SpoolEnabled}, spoolThreshold={SpoolThreshold}]";

    internal string CreateSpoolFilePath()
    {
        var directory = SpoolDirectory;
        lock (_lock)
        {
            if (!_directoryCreated)
            {
                Directory.CreateDirectory(directory);
                _directoryCreated = true;
            }
        }

        return Path.Combine(directory, "cos" + Guid.NewGuid().ToString("N") + ".tmp");
    }

    internal void OnCachedInMemory(long length)
    {
        Interlocked.Increment(ref _memoryCounter);
        Interlocked.Add(ref _memorySize, length);
    }

    internal void OnSpooled(long length)
    {
        Interlocked.Increment(ref _spoolCounter);
        Interlocked.Add(ref _spoolSize, length);
    }

    internal void OnSpoolFileDeleted() => Interlocked.Increment(ref _spoolDeleted);
}
