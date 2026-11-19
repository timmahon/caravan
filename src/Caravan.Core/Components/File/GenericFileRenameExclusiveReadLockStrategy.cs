using Microsoft.Extensions.Logging;

namespace Caravan.Components.File;

/// <summary>
/// The <c>rename</c> read lock (Camel's <c>GenericFileRenameExclusiveReadLockStrategy</c>): the file is taken once it can be renamed (to
/// <c>name.caravanExclusiveReadLock</c> and back), retrying every <see cref="CheckInterval"/> until <see cref="Timeout"/> (zero waits forever).
/// Only works where renaming a file being written fails (Windows, some remote servers).
/// </summary>
/// <typeparam name="T">The type of the underlying file object.</typeparam>
public sealed class GenericFileRenameExclusiveReadLockStrategy<T> : IGenericFileExclusiveReadLockStrategy<T>
{
    /// <summary>The suffix of the temporary name.</summary>
    public const string Suffix = ".caravanExclusiveReadLock";

    /// <summary>The interval between two attempts (default 1s).</summary>
    public TimeSpan CheckInterval { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>How long to try before skipping the file until the next poll (default 10s; zero waits forever).</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>The level of the message logged when a file is skipped (default debug).</summary>
    public LogLevel ReadLockLoggingLevel { get; set; } = LogLevel.Debug;

    /// <inheritdoc />
    public ValueTask PrepareOnStartupAsync(IGenericFileOperations<T> operations, GenericFileEndpoint<T> endpoint) => ValueTask.CompletedTask;

    /// <inheritdoc />
    public async ValueTask<bool> AcquireExclusiveReadLockAsync(IGenericFileOperations<T> operations, GenericFile<T> file, Exchange exchange, CancellationToken cancellationToken)
    {
        var time = exchange.Context.TimeProvider;
        var logger = exchange.Context.LoggerFactory.CreateLogger<GenericFileRenameExclusiveReadLockStrategy<T>>();
        var start = time.GetTimestamp();
        var temp = file.AbsoluteFilePath + Suffix;
        while (true)
        {
            if (Timeout > TimeSpan.Zero && time.GetElapsedTime(start) > Timeout)
            {
                logger.Log(ReadLockLoggingLevel, "Cannot acquire read lock within {Timeout}. Will skip the file: {File}", Timeout, file);
                return false;
            }

            if (operations.RenameFile(file.AbsoluteFilePath, temp))
            {
                if (!operations.RenameFile(temp, file.AbsoluteFilePath))
                {
                    throw new GenericFileOperationFailedException($"Cannot rename file {temp} back to {file.AbsoluteFilePath}");
                }

                return true;
            }

            if (!operations.ExistsFile(file.AbsoluteFilePath))
            {
                logger.Log(ReadLockLoggingLevel, "Cannot acquire read lock as the file no longer exists. Will skip the file: {File}", file);
                return false;
            }

            try
            {
                await Task.Delay(CheckInterval, time, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return false;
            }
        }
    }

    /// <inheritdoc />
    public ValueTask ReleaseExclusiveReadLockOnAbortAsync(IGenericFileOperations<T> operations, GenericFile<T> file, Exchange exchange) => ValueTask.CompletedTask;

    /// <inheritdoc />
    public ValueTask ReleaseExclusiveReadLockOnRollbackAsync(IGenericFileOperations<T> operations, GenericFile<T> file, Exchange exchange) => ValueTask.CompletedTask;

    /// <inheritdoc />
    public ValueTask ReleaseExclusiveReadLockOnCommitAsync(IGenericFileOperations<T> operations, GenericFile<T> file, Exchange exchange) => ValueTask.CompletedTask;
}
