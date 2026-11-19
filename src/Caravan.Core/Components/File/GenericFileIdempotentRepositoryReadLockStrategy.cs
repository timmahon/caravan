using Caravan.Spi;
using Microsoft.Extensions.Logging;

namespace Caravan.Components.File;

/// <summary>
/// The <c>idempotent</c> read lock (Camel's <c>GenericFileIdempotentRepositoryReadLockStrategy</c>): the file is taken by whoever adds its
/// absolute path to <see cref="Repository"/> first, so consumers sharing a (clustered) repository never take the same file. The key is removed
/// on rollback (<see cref="RemoveOnRollback"/>, default true) and kept on commit unless <see cref="RemoveOnCommit"/>.
/// </summary>
/// <typeparam name="T">The type of the underlying file object.</typeparam>
/// <param name="repository">The repository holding the keys of the files taken.</param>
public sealed class GenericFileIdempotentRepositoryReadLockStrategy<T>(IIdempotentRepository repository) : IGenericFileExclusiveReadLockStrategy<T>
{
    private const string KeyProperty = "CaravanFileReadLockKey";

    /// <summary>The repository holding the keys of the files taken.</summary>
    public IIdempotentRepository Repository { get; } = repository;

    /// <summary>Whether to remove the key when the exchange failed, so the file can be taken again (default true).</summary>
    public bool RemoveOnRollback { get; set; } = true;

    /// <summary>Whether to remove the key when the exchange succeeded (default false).</summary>
    public bool RemoveOnCommit { get; set; }

    /// <summary>The level of the message logged when a file is skipped (default debug).</summary>
    public LogLevel ReadLockLoggingLevel { get; set; } = LogLevel.Debug;

    /// <inheritdoc />
    public ValueTask PrepareOnStartupAsync(IGenericFileOperations<T> operations, GenericFileEndpoint<T> endpoint) => ValueTask.CompletedTask;

    /// <inheritdoc />
    public async ValueTask<bool> AcquireExclusiveReadLockAsync(IGenericFileOperations<T> operations, GenericFile<T> file, Exchange exchange, CancellationToken cancellationToken)
    {
        if (await Repository.AddAsync(file.AbsoluteFilePath, cancellationToken).ConfigureAwait(false))
        {
            exchange.SetProperty(KeyProperty, file.AbsoluteFilePath);
            return true;
        }

        exchange.Context.LoggerFactory.CreateLogger<GenericFileIdempotentRepositoryReadLockStrategy<T>>()
            .Log(ReadLockLoggingLevel, "Cannot acquire read lock as the file is already in the idempotent repository. Will skip the file: {File}", file);
        return false;
    }

    /// <inheritdoc />
    public async ValueTask ReleaseExclusiveReadLockOnAbortAsync(IGenericFileOperations<T> operations, GenericFile<T> file, Exchange exchange)
    {
        if (KeyOf(exchange) is { } key)
        {
            await Repository.RemoveAsync(key).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async ValueTask ReleaseExclusiveReadLockOnRollbackAsync(IGenericFileOperations<T> operations, GenericFile<T> file, Exchange exchange)
    {
        if (RemoveOnRollback && KeyOf(exchange) is { } key)
        {
            await Repository.RemoveAsync(key).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async ValueTask ReleaseExclusiveReadLockOnCommitAsync(IGenericFileOperations<T> operations, GenericFile<T> file, Exchange exchange)
    {
        if (KeyOf(exchange) is not { } key)
        {
            return;
        }

        if (RemoveOnCommit)
        {
            await Repository.RemoveAsync(key).ConfigureAwait(false);
        }
        else
        {
            await Repository.ConfirmAsync(key).ConfigureAwait(false);
        }
    }

    // The key this exchange added (the path the file had when it was locked, before any preMove); null when it holds none.
    private static string? KeyOf(Exchange exchange) => exchange.GetProperty<string>(KeyProperty);
}
