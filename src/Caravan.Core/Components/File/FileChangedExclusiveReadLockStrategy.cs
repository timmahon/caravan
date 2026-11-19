using Microsoft.Extensions.Logging;

namespace Caravan.Components.File;

/// <summary>
/// The <c>changed</c> read lock (Camel's <c>FileChangedExclusiveReadLockStrategy</c>): the file is taken once its length and modification time
/// did not change between two checks <see cref="CheckInterval"/> apart and it holds at least <see cref="MinLength"/> bytes; with
/// <see cref="MinAge"/> it is taken as soon as it was last modified at least that long ago instead. Gives up after <see cref="Timeout"/>
/// (zero waits forever). Waits use the context's <see cref="ICaravanContext.TimeProvider"/>.
/// </summary>
public sealed class FileChangedExclusiveReadLockStrategy : IGenericFileExclusiveReadLockStrategy<FileInfo>
{
    /// <summary>The interval between two checks (default 1s).</summary>
    public TimeSpan CheckInterval { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>How long to try before skipping the file until the next poll (default 10s; zero waits forever).</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>The minimum length of the file (default 1 byte).</summary>
    public long MinLength { get; set; } = 1;

    /// <summary>The minimum time since the last modification (default zero: compare two checks instead).</summary>
    public TimeSpan MinAge { get; set; }

    /// <summary>The level of the message logged when a file is skipped (default debug).</summary>
    public LogLevel ReadLockLoggingLevel { get; set; } = LogLevel.Debug;

    /// <inheritdoc />
    public ValueTask PrepareOnStartupAsync(IGenericFileOperations<FileInfo> operations, GenericFileEndpoint<FileInfo> endpoint) => ValueTask.CompletedTask;

    /// <inheritdoc />
    public async ValueTask<bool> AcquireExclusiveReadLockAsync(
        IGenericFileOperations<FileInfo> operations,
        GenericFile<FileInfo> file,
        Exchange exchange,
        CancellationToken cancellationToken)
    {
        var time = exchange.Context.TimeProvider;
        var logger = exchange.Context.LoggerFactory.CreateLogger<FileChangedExclusiveReadLockStrategy>();
        var start = time.GetTimestamp();
        var lastModified = long.MinValue;
        var length = long.MinValue;
        while (true)
        {
            if (Timeout > TimeSpan.Zero && time.GetElapsedTime(start) > Timeout)
            {
                logger.Log(ReadLockLoggingLevel, "Cannot acquire read lock within {Timeout}. Will skip the file: {File}", Timeout, file);
                return false;
            }

            if (operations.GetFile(file.AbsoluteFilePath) is not { } current)
            {
                logger.Log(ReadLockLoggingLevel, "Cannot acquire read lock as the file no longer exists. Will skip the file: {File}", file);
                return false;
            }

            var newLastModified = current.LastModified;
            var newLength = current.FileLength;
            var olderThan = time.GetUtcNow().ToUnixTimeMilliseconds() - (long)MinAge.TotalMilliseconds;
            if (newLength >= MinLength &&
                ((MinAge == TimeSpan.Zero && newLastModified == lastModified && newLength == length) ||
                 (MinAge > TimeSpan.Zero && newLastModified < olderThan)))
            {
                return true;
            }

            lastModified = newLastModified;
            length = newLength;
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
    public ValueTask ReleaseExclusiveReadLockOnAbortAsync(IGenericFileOperations<FileInfo> operations, GenericFile<FileInfo> file, Exchange exchange) =>
        ValueTask.CompletedTask;

    /// <inheritdoc />
    public ValueTask ReleaseExclusiveReadLockOnRollbackAsync(IGenericFileOperations<FileInfo> operations, GenericFile<FileInfo> file, Exchange exchange) =>
        ValueTask.CompletedTask;

    /// <inheritdoc />
    public ValueTask ReleaseExclusiveReadLockOnCommitAsync(IGenericFileOperations<FileInfo> operations, GenericFile<FileInfo> file, Exchange exchange) =>
        ValueTask.CompletedTask;
}
