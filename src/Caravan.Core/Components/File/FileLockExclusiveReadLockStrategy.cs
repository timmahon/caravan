using Microsoft.Extensions.Logging;

namespace Caravan.Components.File;

/// <summary>
/// The <c>fileLock</c> read lock (Camel's <c>FileLockExclusiveReadLockStrategy</c>): opens the file with <see cref="FileShare.None"/>, an
/// exclusive OS lock (on Linux and macOS an advisory <c>flock</c>, which .NET writers and other processes using locks respect), retrying every
/// <see cref="CheckInterval"/> until <see cref="Timeout"/> (zero waits forever). The handle stays open, and the body is read through it, until
/// the file is released.
/// </summary>
public sealed class FileLockExclusiveReadLockStrategy : IGenericFileExclusiveReadLockStrategy<FileInfo>
{
    private const string LockProperty = "CaravanFileLock";

    /// <summary>The interval between two attempts (default 1s).</summary>
    public TimeSpan CheckInterval { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>How long to try before skipping the file until the next poll (default 10s; zero waits forever).</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(10);

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
        var logger = exchange.Context.LoggerFactory.CreateLogger<FileLockExclusiveReadLockStrategy>();
        var start = time.GetTimestamp();
        while (true)
        {
            if (Timeout > TimeSpan.Zero && time.GetElapsedTime(start) > Timeout)
            {
                logger.Log(ReadLockLoggingLevel, "Cannot acquire read lock within {Timeout}. Will skip the file: {File}", Timeout, file);
                return false;
            }

            try
            {
                var stream = new FileStream(file.AbsoluteFilePath, FileMode.Open, FileAccess.Read, FileShare.None, 1, FileOptions.None);
                exchange.SetProperty(LockProperty, stream);
                var handle = stream.SafeFileHandle;
                file.ContentOpener = f => handle.IsClosed ? FileOperations.Open(f.AbsoluteFilePath) : new LockedFileStream(handle);
                return true;
            }
            catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
            {
                logger.Log(ReadLockLoggingLevel, "Cannot acquire read lock as the file no longer exists. Will skip the file: {File}", file);
                return false;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                logger.LogTrace(e, "Cannot acquire read lock on {File}, will try again", file);
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
    public ValueTask ReleaseExclusiveReadLockOnAbortAsync(IGenericFileOperations<FileInfo> operations, GenericFile<FileInfo> file, Exchange exchange) =>
        ReleaseAsync(exchange);

    /// <inheritdoc />
    public ValueTask ReleaseExclusiveReadLockOnRollbackAsync(IGenericFileOperations<FileInfo> operations, GenericFile<FileInfo> file, Exchange exchange) =>
        ReleaseAsync(exchange);

    /// <inheritdoc />
    public ValueTask ReleaseExclusiveReadLockOnCommitAsync(IGenericFileOperations<FileInfo> operations, GenericFile<FileInfo> file, Exchange exchange) =>
        ReleaseAsync(exchange);

    private static async ValueTask ReleaseAsync(Exchange exchange)
    {
        if (exchange.GetProperty(LockProperty) is FileStream stream)
        {
            exchange.RemoveProperty(LockProperty);
            await stream.DisposeAsync().ConfigureAwait(false);
        }
    }
}
