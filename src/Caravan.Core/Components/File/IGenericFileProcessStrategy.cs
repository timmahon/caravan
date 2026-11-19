namespace Caravan.Components.File;

/// <summary>
/// What a file consumer does with a file around its processing (Camel's <c>GenericFileProcessStrategy</c>): <see cref="BeginAsync"/> before
/// routing (read lock, <c>preMove</c>), then <see cref="CommitAsync"/> (<c>move</c>, <c>delete</c>) or <see cref="RollbackAsync"/>
/// (<c>moveFailed</c>) when the exchange's unit of work completes, or <see cref="AbortAsync"/> when the file is not processed after all.
/// Custom strategies are set with <c>processStrategy=#bean</c>.
/// </summary>
/// <typeparam name="T">The type of the underlying file object.</typeparam>
public interface IGenericFileProcessStrategy<T>
{
    /// <summary>Called once before the first poll.</summary>
    /// <param name="operations">The file operations.</param>
    /// <param name="endpoint">The endpoint.</param>
    ValueTask PrepareOnStartupAsync(IGenericFileOperations<T> operations, GenericFileEndpoint<T> endpoint);

    /// <summary>Prepares the file for processing; false skips it (e.g. the read lock was not acquired).</summary>
    /// <param name="operations">The file operations.</param>
    /// <param name="endpoint">The endpoint.</param>
    /// <param name="exchange">The exchange created for the file; a strategy moving the file binds the moved file to it.</param>
    /// <param name="file">The file.</param>
    /// <param name="cancellationToken">Cancels waiting for a read lock.</param>
    ValueTask<bool> BeginAsync(IGenericFileOperations<T> operations, GenericFileEndpoint<T> endpoint, Exchange exchange, GenericFile<T> file, CancellationToken cancellationToken);

    /// <summary>Undoes <see cref="BeginAsync"/> when the file is not processed.</summary>
    /// <param name="operations">The file operations.</param>
    /// <param name="endpoint">The endpoint.</param>
    /// <param name="exchange">The exchange.</param>
    /// <param name="file">The file.</param>
    ValueTask AbortAsync(IGenericFileOperations<T> operations, GenericFileEndpoint<T> endpoint, Exchange exchange, GenericFile<T> file);

    /// <summary>Completes a successful exchange.</summary>
    /// <param name="operations">The file operations.</param>
    /// <param name="endpoint">The endpoint.</param>
    /// <param name="exchange">The exchange.</param>
    /// <param name="file">The file (after <c>preMove</c>).</param>
    ValueTask CommitAsync(IGenericFileOperations<T> operations, GenericFileEndpoint<T> endpoint, Exchange exchange, GenericFile<T> file);

    /// <summary>Completes a failed exchange.</summary>
    /// <param name="operations">The file operations.</param>
    /// <param name="endpoint">The endpoint.</param>
    /// <param name="exchange">The exchange.</param>
    /// <param name="file">The file (after <c>preMove</c>).</param>
    ValueTask RollbackAsync(IGenericFileOperations<T> operations, GenericFileEndpoint<T> endpoint, Exchange exchange, GenericFile<T> file);
}
