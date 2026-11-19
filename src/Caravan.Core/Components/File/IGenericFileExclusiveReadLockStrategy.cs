namespace Caravan.Components.File;

/// <summary>
/// Gives a file consumer exclusive access to a file before processing it, so a file still being written is not picked up (Camel's
/// <c>GenericFileExclusiveReadLockStrategy</c>; the <c>readLock</c> option). Acquired before the process strategy's begin, released before
/// its commit or rollback moves or deletes the file.
/// </summary>
/// <typeparam name="T">The type of the underlying file object.</typeparam>
public interface IGenericFileExclusiveReadLockStrategy<T>
{
    /// <summary>Called once before the first poll, e.g. to delete orphan lock files left by a crash.</summary>
    /// <param name="operations">The file operations.</param>
    /// <param name="endpoint">The endpoint.</param>
    ValueTask PrepareOnStartupAsync(IGenericFileOperations<T> operations, GenericFileEndpoint<T> endpoint);

    /// <summary>Tries to acquire the lock, possibly waiting (on the context's clock); false skips the file until the next poll.</summary>
    /// <param name="operations">The file operations.</param>
    /// <param name="file">The file.</param>
    /// <param name="exchange">The exchange created for the file.</param>
    /// <param name="cancellationToken">Cancels the wait.</param>
    ValueTask<bool> AcquireExclusiveReadLockAsync(IGenericFileOperations<T> operations, GenericFile<T> file, Exchange exchange, CancellationToken cancellationToken);

    /// <summary>Releases the lock when the file was not processed after all.</summary>
    /// <param name="operations">The file operations.</param>
    /// <param name="file">The file.</param>
    /// <param name="exchange">The exchange.</param>
    ValueTask ReleaseExclusiveReadLockOnAbortAsync(IGenericFileOperations<T> operations, GenericFile<T> file, Exchange exchange);

    /// <summary>Releases the lock after the exchange failed.</summary>
    /// <param name="operations">The file operations.</param>
    /// <param name="file">The file.</param>
    /// <param name="exchange">The exchange.</param>
    ValueTask ReleaseExclusiveReadLockOnRollbackAsync(IGenericFileOperations<T> operations, GenericFile<T> file, Exchange exchange);

    /// <summary>Releases the lock after the exchange succeeded.</summary>
    /// <param name="operations">The file operations.</param>
    /// <param name="file">The file.</param>
    /// <param name="exchange">The exchange.</param>
    ValueTask ReleaseExclusiveReadLockOnCommitAsync(IGenericFileOperations<T> operations, GenericFile<T> file, Exchange exchange);
}
