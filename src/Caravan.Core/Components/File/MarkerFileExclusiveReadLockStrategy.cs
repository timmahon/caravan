using Microsoft.Extensions.Logging;

namespace Caravan.Components.File;

/// <summary>
/// The <c>markerFile</c> read lock (the default; Camel's <c>MarkerFileExclusiveReadLockStrategy</c>): creates <c>name.caravanLock</c> next to the
/// file (atomically, failing when another consumer holds it) and deletes it when the file is released. Other strategies (<c>changed</c>,
/// <c>fileLock</c>, <c>rename</c>) run inside it as <see cref="Inner"/> unless <c>readLockMarkerFile=false</c>. With
/// <see cref="DeleteOrphanLockFiles"/> (default true) lock files left by a crash are deleted before the first poll.
/// </summary>
public sealed class MarkerFileExclusiveReadLockStrategy : IGenericFileExclusiveReadLockStrategy<FileInfo>
{
    /// <summary>The suffix of marker files (Camel's <c>.camelLock</c>).</summary>
    public const string LockFileSuffix = ".caravanLock";

    /// <summary>Creates the strategy.</summary>
    /// <param name="inner">A strategy acquired after the marker file and released before it, or null.</param>
    public MarkerFileExclusiveReadLockStrategy(IGenericFileExclusiveReadLockStrategy<FileInfo>? inner = null)
    {
        Inner = inner;
    }

    /// <summary>The strategy acquired after the marker file, if any.</summary>
    public IGenericFileExclusiveReadLockStrategy<FileInfo>? Inner { get; }

    /// <summary>Whether to delete orphan marker files before the first poll (default true).</summary>
    public bool DeleteOrphanLockFiles { get; set; } = true;

    /// <summary>The level of the message logged when a file is skipped because it is locked (default debug).</summary>
    public LogLevel ReadLockLoggingLevel { get; set; } = LogLevel.Debug;

    /// <inheritdoc />
    public async ValueTask PrepareOnStartupAsync(IGenericFileOperations<FileInfo> operations, GenericFileEndpoint<FileInfo> endpoint)
    {
        if (DeleteOrphanLockFiles)
        {
            var logger = endpoint.Context.LoggerFactory.CreateLogger<MarkerFileExclusiveReadLockStrategy>();
            DeleteLockFiles(endpoint, endpoint.Directory, 1, logger);
        }

        if (Inner is not null)
        {
            await Inner.PrepareOnStartupAsync(operations, endpoint).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async ValueTask<bool> AcquireExclusiveReadLockAsync(
        IGenericFileOperations<FileInfo> operations,
        GenericFile<FileInfo> file,
        Exchange exchange,
        CancellationToken cancellationToken)
    {
        var lockFile = file.AbsoluteFilePath + LockFileSuffix;
        try
        {
            await using (new FileStream(lockFile, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
            }
        }
        catch (IOException e) when (e is not FileNotFoundException and not DirectoryNotFoundException)
        {
            exchange.Context.LoggerFactory.CreateLogger<MarkerFileExclusiveReadLockStrategy>()
                .Log(ReadLockLoggingLevel, "Cannot acquire read lock as the lock file {LockFile} exists. Will skip the file: {File}", lockFile, file);
            return false;
        }
        catch (Exception e) when (e is DirectoryNotFoundException or FileNotFoundException or UnauthorizedAccessException)
        {
            exchange.Context.LoggerFactory.CreateLogger<MarkerFileExclusiveReadLockStrategy>()
                .Log(ReadLockLoggingLevel, e, "Cannot create the lock file {LockFile}. Will skip the file: {File}", lockFile, file);
            return false;
        }

        exchange.SetProperty(ExchangePropertyKeys.FileLockFileName, lockFile);
        if (Inner is null)
        {
            return true;
        }

        bool acquired;
        try
        {
            acquired = await Inner.AcquireExclusiveReadLockAsync(operations, file, exchange, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            DeleteMarker(exchange, lockFile);
            throw;
        }

        if (!acquired)
        {
            DeleteMarker(exchange, lockFile);
        }

        return acquired;
    }

    /// <inheritdoc />
    public async ValueTask ReleaseExclusiveReadLockOnAbortAsync(IGenericFileOperations<FileInfo> operations, GenericFile<FileInfo> file, Exchange exchange)
    {
        try
        {
            if (Inner is not null)
            {
                await Inner.ReleaseExclusiveReadLockOnAbortAsync(operations, file, exchange).ConfigureAwait(false);
            }
        }
        finally
        {
            Release(file, exchange);
        }
    }

    /// <inheritdoc />
    public async ValueTask ReleaseExclusiveReadLockOnRollbackAsync(IGenericFileOperations<FileInfo> operations, GenericFile<FileInfo> file, Exchange exchange)
    {
        try
        {
            if (Inner is not null)
            {
                await Inner.ReleaseExclusiveReadLockOnRollbackAsync(operations, file, exchange).ConfigureAwait(false);
            }
        }
        finally
        {
            Release(file, exchange);
        }
    }

    /// <inheritdoc />
    public async ValueTask ReleaseExclusiveReadLockOnCommitAsync(IGenericFileOperations<FileInfo> operations, GenericFile<FileInfo> file, Exchange exchange)
    {
        try
        {
            if (Inner is not null)
            {
                await Inner.ReleaseExclusiveReadLockOnCommitAsync(operations, file, exchange).ConfigureAwait(false);
            }
        }
        finally
        {
            Release(file, exchange);
        }
    }

    // Only the marker this exchange created is deleted (a failed acquisition must not delete another consumer's).
    private static void Release(GenericFile<FileInfo> file, Exchange exchange)
    {
        if (exchange.GetProperty<string>(ExchangePropertyKeys.FileLockFileName) is { } lockFile)
        {
            DeleteMarker(exchange, lockFile);
        }
    }

    private static void DeleteMarker(Exchange exchange, string lockFile)
    {
        exchange.RemoveProperty(ExchangePropertyKeys.FileLockFileName);
        try
        {
            System.IO.File.Delete(lockFile);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            exchange.Context.LoggerFactory.CreateLogger<MarkerFileExclusiveReadLockStrategy>().LogWarning(e, "Cannot delete the lock file {LockFile}", lockFile);
        }
    }

    private static void DeleteLockFiles(GenericFileEndpoint<FileInfo> endpoint, string directory, int depth, ILogger logger)
    {
        IEnumerable<FileSystemInfo> entries;
        try
        {
            entries = new DirectoryInfo(directory).EnumerateFileSystemInfos().ToList();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return;
        }

        foreach (var entry in entries)
        {
            if (entry is DirectoryInfo dir)
            {
                if (endpoint.Recursive && depth < endpoint.MaxDepth && (endpoint.IncludeHiddenDirs || !dir.Name.StartsWith('.')))
                {
                    DeleteLockFiles(endpoint, dir.FullName, depth + 1, logger);
                }
            }
            else if (entry.Name.EndsWith(LockFileSuffix, StringComparison.Ordinal))
            {
                logger.LogWarning("Deleting orphaned lock file: {LockFile}", entry.FullName);
                try
                {
                    entry.Delete();
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    logger.LogWarning(e, "Cannot delete the orphaned lock file {LockFile}", entry.FullName);
                }
            }
        }
    }
}
