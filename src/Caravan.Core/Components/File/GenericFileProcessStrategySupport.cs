namespace Caravan.Components.File;

/// <summary>
/// Base class of the process strategies (Camel's <c>GenericFileProcessStrategySupport</c>): acquires the exclusive read lock in
/// <see cref="BeginAsync"/> and releases it (and what the operations retrieved) first thing in <see cref="AbortAsync"/>,
/// <see cref="CommitAsync"/> and <see cref="RollbackAsync"/>, before the file is moved or deleted.
/// </summary>
/// <typeparam name="T">The type of the underlying file object.</typeparam>
public abstract class GenericFileProcessStrategySupport<T> : IGenericFileProcessStrategy<T>
{
    /// <summary>The read lock, or null for none.</summary>
    public IGenericFileExclusiveReadLockStrategy<T>? ExclusiveReadLockStrategy { get; set; }

    /// <inheritdoc />
    public virtual ValueTask PrepareOnStartupAsync(IGenericFileOperations<T> operations, GenericFileEndpoint<T> endpoint) =>
        ExclusiveReadLockStrategy?.PrepareOnStartupAsync(operations, endpoint) ?? ValueTask.CompletedTask;

    /// <inheritdoc />
    public virtual ValueTask<bool> BeginAsync(
        IGenericFileOperations<T> operations,
        GenericFileEndpoint<T> endpoint,
        Exchange exchange,
        GenericFile<T> file,
        CancellationToken cancellationToken) =>
        ExclusiveReadLockStrategy?.AcquireExclusiveReadLockAsync(operations, file, exchange, cancellationToken) ?? ValueTask.FromResult(true);

    /// <inheritdoc />
    public virtual async ValueTask AbortAsync(IGenericFileOperations<T> operations, GenericFileEndpoint<T> endpoint, Exchange exchange, GenericFile<T> file)
    {
        operations.ReleaseRetrievedFileResources(exchange);
        if (ExclusiveReadLockStrategy is not null)
        {
            await ExclusiveReadLockStrategy.ReleaseExclusiveReadLockOnAbortAsync(operations, file, exchange).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public virtual async ValueTask CommitAsync(IGenericFileOperations<T> operations, GenericFileEndpoint<T> endpoint, Exchange exchange, GenericFile<T> file)
    {
        operations.ReleaseRetrievedFileResources(exchange);
        if (ExclusiveReadLockStrategy is not null)
        {
            await ExclusiveReadLockStrategy.ReleaseExclusiveReadLockOnCommitAsync(operations, file, exchange).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public virtual async ValueTask RollbackAsync(IGenericFileOperations<T> operations, GenericFileEndpoint<T> endpoint, Exchange exchange, GenericFile<T> file)
    {
        operations.ReleaseRetrievedFileResources(exchange);
        if (ExclusiveReadLockStrategy is not null)
        {
            await ExclusiveReadLockStrategy.ReleaseExclusiveReadLockOnRollbackAsync(operations, file, exchange).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Moves <paramref name="file"/> to the name <paramref name="renamer"/> evaluates to (Camel's <c>GenericFileExpressionRenamer</c>), on a copy
    /// of <paramref name="exchange"/> carrying the file's headers; returns the moved file. An existing target is replaced, missing directories
    /// are created.
    /// </summary>
    /// <param name="operations">The file operations.</param>
    /// <param name="exchange">The exchange.</param>
    /// <param name="file">The file to move.</param>
    /// <param name="renamer">The expression giving the new name (relative to the starting directory, or absolute).</param>
    /// <exception cref="GenericFileOperationFailedException">The file cannot be moved.</exception>
    protected static GenericFile<T> Rename(IGenericFileOperations<T> operations, Exchange exchange, GenericFile<T> file, IExpression renamer)
    {
        var copy = exchange.Copy();
        file.BindToExchange(copy);
        var newName = exchange.Context.TypeConverter.Convert(typeof(string), copy, renamer.Evaluate(copy)) as string;
        if (string.IsNullOrEmpty(newName))
        {
            throw new GenericFileOperationFailedException($"The expression {renamer} gave no file name to move {file} to");
        }

        var to = file.Copy();
        to.ChangeFileName(newName);
        return RenameFile(operations, file, to);
    }

    /// <summary>Renames <paramref name="from"/> to <paramref name="to"/>, replacing an existing file and creating missing directories.</summary>
    /// <param name="operations">The file operations.</param>
    /// <param name="from">The file.</param>
    /// <param name="to">The target.</param>
    /// <exception cref="GenericFileOperationFailedException">The file cannot be renamed.</exception>
    protected static GenericFile<T> RenameFile(IGenericFileOperations<T> operations, GenericFile<T> from, GenericFile<T> to)
    {
        operations.DeleteFile(to.AbsoluteFilePath);
        if (Path.GetDirectoryName(to.AbsoluteFilePath) is { Length: > 0 } directory && !operations.BuildDirectory(directory, true))
        {
            throw new GenericFileOperationFailedException($"Cannot create directory: {directory} (could be because of denied permissions)");
        }

        if (!operations.RenameFile(from.AbsoluteFilePath, to.AbsoluteFilePath))
        {
            throw new GenericFileOperationFailedException($"Cannot rename file: {from.AbsoluteFilePath} to: {to.AbsoluteFilePath}");
        }

        return to;
    }
}
