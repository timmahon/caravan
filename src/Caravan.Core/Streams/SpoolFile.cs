using Caravan.Spi;

namespace Caravan.Streams;

/// <summary>
/// A temporary file holding spooled stream cache data (Camel's <c>TempFileManager</c>). Deleted when the exchange that created it completes
/// (it is registered as a synchronization on that exchange), or when the strategy's spool directory is removed. Readers opened before the
/// deletion keep working: files are opened with <see cref="FileShare.Delete"/>.
/// </summary>
internal sealed class SpoolFile(string path, long length, StreamCachingStrategy strategy) : ISynchronization
{
    private int _deleted;

    public string Path { get; } = path;

    public long Length { get; } = length;

    public bool IsDeleted => Volatile.Read(ref _deleted) == 1;

    public FileStream OpenRead()
    {
        if (IsDeleted)
        {
            throw new ObjectDisposedException(Path, "The spooled stream cache file was deleted when its exchange completed");
        }

        return new FileStream(Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.Asynchronous);
    }

    public void Delete()
    {
        if (Interlocked.Exchange(ref _deleted, 1) == 1)
        {
            return;
        }

        try
        {
            File.Delete(Path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Best effort: the spool directory is removed when the context stops.
        }

        strategy.OnSpoolFileDeleted();
    }

    public ValueTask OnCompleteAsync(Exchange exchange)
    {
        Delete();
        return ValueTask.CompletedTask;
    }

    public ValueTask OnFailureAsync(Exchange exchange)
    {
        Delete();
        return ValueTask.CompletedTask;
    }

    public override string ToString() => $"SpoolFile[{Path}]";
}
