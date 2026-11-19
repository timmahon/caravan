using Caravan.Health;
using Caravan.Spi;
using Caravan.Support;
using Microsoft.Extensions.Logging;

namespace Caravan.Components.File;

/// <summary>
/// The consumer of file components (Camel's <c>GenericFileConsumer</c>), a <see cref="ScheduledPollConsumer"/>. Each poll lists the starting
/// directory (recursively with <c>recursive</c>, within <c>minDepth</c>/<c>maxDepth</c>), keeps the files that match the filters, are not in
/// progress and, when idempotent, not processed yet; sorts them (<c>sorter</c>, <c>sortBy</c>, <c>shuffle</c>), limits them
/// (<c>maxMessagesPerPoll</c>) and processes them one by one as a batch (<c>CaravanBatchIndex</c>, <c>CaravanBatchSize</c>,
/// <c>CaravanBatchComplete</c>): the process strategy begins (read lock, <c>preMove</c>), the file is retrieved and routed, and a
/// <see cref="ISynchronization"/> commits or rolls back when the exchange's unit of work completes. Files that disappeared are skipped.
/// On graceful shutdown the file being processed completes; with <see cref="ShutdownRunningTask.CompleteAllTasks"/> the rest of the batch too.
/// Its health is Down while the starting directory is missing or the last poll failed.
/// </summary>
/// <typeparam name="T">The type of the underlying file object.</typeparam>
public class GenericFileConsumer<T> : ScheduledPollConsumer, IHealthCheck
{
    private readonly GenericFileEndpoint<T> _endpoint;
    private readonly IExceptionHandler _exceptionHandler;
    private bool _prepared;
    private int _shutdownRunningTask = -1;
    private int _pendingExchanges;
    private volatile bool _polling;
    private volatile Exception? _lastPollError;
    private string? _fileExpressionResult;

    /// <summary>Creates the consumer.</summary>
    /// <param name="endpoint">The endpoint.</param>
    /// <param name="processor">The route's processor.</param>
    /// <param name="operations">The file operations.</param>
    public GenericFileConsumer(GenericFileEndpoint<T> endpoint, IProcessor processor, IGenericFileOperations<T> operations)
        : base(endpoint, processor)
    {
        _endpoint = endpoint;
        Operations = operations;
        _exceptionHandler = new LoggingExceptionHandler(Logger);
    }

    /// <summary>The endpoint.</summary>
    public GenericFileEndpoint<T> GenericFileEndpoint => _endpoint;

    /// <summary>The file operations.</summary>
    public IGenericFileOperations<T> Operations { get; }

    /// <summary>The error of the last poll, or null when it succeeded.</summary>
    public Exception? LastPollError => _lastPollError;

    /// <inheritdoc />
    public override int PendingExchangesSize
    {
        get
        {
            var answer = _shutdownRunningTask == (int)ShutdownRunningTask.CompleteAllTasks ? Volatile.Read(ref _pendingExchanges) : 0;

            // Between two files of a batch there is a moment without inflight exchange.
            return answer == 0 && _polling ? 1 : answer;
        }
    }

    /// <inheritdoc />
    string IHealthCheck.Id => "consumer:" + _endpoint.EndpointUri;

    /// <inheritdoc />
    string IHealthCheck.Group => "consumers";

    /// <inheritdoc />
    public override bool DeferShutdown(ShutdownRunningTask runningTask)
    {
        _shutdownRunningTask = (int)runningTask;
        return false;
    }

    /// <inheritdoc />
    public override void PrepareShutdown(ShutdownRunningTask runningTask, bool suspendOnly, bool forced)
    {
        _shutdownRunningTask = (int)runningTask;
        base.PrepareShutdown(runningTask, suspendOnly, forced);
    }

    /// <inheritdoc />
    public ValueTask<HealthCheckResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        var details = new Dictionary<string, object?> { ["directory"] = _endpoint.Directory };
        HealthCheckResult result;
        if (!Operations.ExistsDirectory(_endpoint.Directory))
        {
            result = new HealthCheckResult("consumer:" + _endpoint.EndpointUri, HealthState.Down)
            {
                Message = $"Directory does not exist: {_endpoint.Directory}",
                Details = details,
            };
        }
        else if (_lastPollError is { } error)
        {
            result = new HealthCheckResult("consumer:" + _endpoint.EndpointUri, HealthState.Down)
            {
                Message = $"The last poll of {_endpoint.Directory} failed: {error.Message}",
                Error = error,
                Details = details,
            };
        }
        else
        {
            result = new HealthCheckResult("consumer:" + _endpoint.EndpointUri, HealthState.Up) { Details = details };
        }

        return ValueTask.FromResult(result);
    }

    /// <summary>Creates the starting directory (<c>autoCreate</c>) or fails (<c>startingDirectoryMustExist</c>), then starts polling.</summary>
    /// <param name="cancellationToken">Cancels the start.</param>
    protected override async Task DoStartAsync(CancellationToken cancellationToken)
    {
        var directory = _endpoint.Directory;
        if (!Operations.ExistsDirectory(directory))
        {
            if (_endpoint.AutoCreate)
            {
                if (!Operations.BuildDirectory(directory, Path.IsPathRooted(directory)))
                {
                    Logger.LogWarning("Cannot auto create starting directory: {Directory}", directory);
                }
            }
            else if (_endpoint.StartingDirectoryMustExist)
            {
                throw new DirectoryNotFoundException($"Starting directory does not exist: {directory}");
            }
        }

        _prepared = false;
        _lastPollError = null;
        await base.DoStartAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    protected override async Task<int> PollAsync(CancellationToken cancellationToken)
    {
        _shutdownRunningTask = -1;
        _pendingExchanges = 0;
        _fileExpressionResult = null;
        _polling = true;
        try
        {
            if (!_prepared)
            {
                await _endpoint.GenericFileProcessStrategy.PrepareOnStartupAsync(Operations, _endpoint).ConfigureAwait(false);
                _prepared = true;
            }

            var files = new List<GenericFile<T>>();
            bool limitHit;
            try
            {
                limitHit = !await PollDirectoryAsync(_endpoint.Directory, files, 0).ConfigureAwait(false);
            }
            catch
            {
                foreach (var file in files)
                {
                    await _endpoint.InProgressRepository.RemoveAsync(file.AbsoluteFilePath, CancellationToken.None).ConfigureAwait(false);
                }

                throw;
            }

            if (limitHit)
            {
                Logger.LogDebug("Limiting maximum messages to poll at {Max} files as there are more messages in this poll", _endpoint.MaxMessagesPerPoll);
            }

            if (_endpoint.Sorter is { } sorter)
            {
                files = [.. files.Order(sorter)];
            }

            var exchanges = new List<(Exchange Exchange, GenericFile<T> File)>(files.Count);
            foreach (var file in files)
            {
                exchanges.Add((_endpoint.CreateExchange(file), file));
            }

            if (_endpoint.SortByComparer is { } sortBy)
            {
                exchanges = [.. exchanges.OrderBy(e => e.Exchange, sortBy)];
            }

            if (_endpoint.Shuffle)
            {
                var array = exchanges.ToArray();
                Random.Shared.Shuffle(array);
                exchanges = [.. array];
            }

            var polled = await ProcessBatchAsync(new Queue<(Exchange, GenericFile<T>)>(exchanges), cancellationToken).ConfigureAwait(false);
            _lastPollError = null;
            return polled;
        }
        catch (Exception e) when (e is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _lastPollError = e;
            throw;
        }
        finally
        {
            _polling = false;
        }
    }

    /// <summary>
    /// Whether <paramref name="file"/> matches the filters (Camel's <c>isMatched</c>): names starting with a dot and lock files are skipped, then
    /// <c>filter</c>, the Ant patterns, <c>filterDirectory</c> (directories stop here), <c>exclude</c>/<c>excludeExt</c>,
    /// <c>include</c>/<c>includeExt</c>, <c>fileName</c>, <c>filterFile</c> and the done file.
    /// </summary>
    /// <param name="file">The file or directory.</param>
    /// <param name="isDirectory">Whether it is a directory.</param>
    protected virtual bool IsMatched(GenericFile<T> file, bool isDirectory)
    {
        var name = file.FileNameOnly;
        if (name.StartsWith('.') && !(isDirectory ? _endpoint.IncludeHiddenDirs : _endpoint.IncludeHiddenFiles))
        {
            return false;
        }

        if (name.EndsWith(MarkerFileExclusiveReadLockStrategy.LockFileSuffix, StringComparison.Ordinal))
        {
            return false;
        }

        if (_endpoint.Filter is { } filter && !filter.Accept(file))
        {
            return false;
        }

        if (_endpoint.AntFilter is { } antFilter && !antFilter.Accept(file))
        {
            return false;
        }

        if (isDirectory)
        {
            return _endpoint.FilterDirectoryPredicate is not { } filterDirectory || filterDirectory.Matches(_endpoint.CreateExchange(file));
        }

        if (_endpoint.ExcludePattern is { } exclude && exclude.IsMatch(name))
        {
            return false;
        }

        if (_endpoint.ExcludeExt is { } excludeExt && HasExtension(name, excludeExt))
        {
            return false;
        }

        if (_endpoint.IncludePattern is { } include && !include.IsMatch(name))
        {
            return false;
        }

        if (_endpoint.IncludeExt is { } includeExt && !HasExtension(name, includeExt))
        {
            return false;
        }

        if (_endpoint.FileNameExpression is { } fileName)
        {
            _fileExpressionResult ??= FileUtil.Normalize(
                _endpoint.Context.TypeConverter.Convert(typeof(string), null, fileName.Evaluate(_endpoint.CreateExchange())) as string ?? string.Empty);
            if (!string.Equals(name, _fileExpressionResult, StringComparison.Ordinal) &&
                !string.Equals(file.RelativeFilePath, _fileExpressionResult, StringComparison.Ordinal))
            {
                return false;
            }
        }

        if (_endpoint.FilterFilePredicate is { } filterFile && !filterFile.Matches(_endpoint.CreateExchange(file)))
        {
            return false;
        }

        if (_endpoint.DoneFileName is not null)
        {
            if (_endpoint.IsDoneFile(name))
            {
                return false;
            }

            if (!Operations.ExistsFile(_endpoint.CreateDoneFileName(file.AbsoluteFilePath)))
            {
                return false;
            }
        }

        return true;
    }

    private static bool HasExtension(string name, string extensions) =>
        extensions.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(ext => name.EndsWith("." + ext.TrimStart('.'), StringComparison.OrdinalIgnoreCase));

    private async Task<bool> PollDirectoryAsync(string directory, List<GenericFile<T>> files, int depth)
    {
        depth++;
        if (depth == 1 && !Operations.ExistsDirectory(directory))
        {
            if (_endpoint.DirectoryMustExist)
            {
                throw new GenericFileOperationFailedException($"Directory does not exist: {directory}");
            }

            Logger.LogDebug("Cannot poll as the directory does not exist: {Directory}", directory);
            return true;
        }

        IEnumerable<GenericFile<T>> entries = Operations.ListFiles(directory);
        if (_endpoint.PreSort)
        {
            entries = entries.OrderBy(e => e.AbsoluteFilePath, StringComparer.Ordinal);
        }

        foreach (var entry in entries)
        {
            if (_endpoint.EagerMaxMessagesPerPoll && _endpoint.MaxMessagesPerPoll > 0 && files.Count >= _endpoint.MaxMessagesPerPoll)
            {
                return false;
            }

            if (entry.IsDirectory)
            {
                if (_endpoint.Recursive && depth < _endpoint.MaxDepth && await IsValidFileAsync(entry, true).ConfigureAwait(false) &&
                    !await PollDirectoryAsync(entry.AbsoluteFilePath, files, depth).ConfigureAwait(false))
                {
                    return false;
                }
            }
            else if (depth >= _endpoint.MinDepth && await IsValidFileAsync(entry, false).ConfigureAwait(false))
            {
                files.Add(entry);
            }
        }

        return true;
    }

    private async Task<bool> IsValidFileAsync(GenericFile<T> file, bool isDirectory)
    {
        if (!IsMatched(file, isDirectory))
        {
            return false;
        }

        if (isDirectory)
        {
            return true;
        }

        // Taking the file in progress first makes it ours: no other poll (or consumer of this endpoint) gets it until it is completed.
        if (!await _endpoint.InProgressRepository.AddAsync(file.AbsoluteFilePath).ConfigureAwait(false))
        {
            Logger.LogTrace("Skipping file {File} as it is already in progress", file);
            return false;
        }

        if (_endpoint.IsIdempotent && IdempotentKey(file) is { } key &&
            await _endpoint.GetOrCreateIdempotentRepository().ContainsAsync(key).ConfigureAwait(false))
        {
            Logger.LogTrace("Skipping file {File} as it was already processed (idempotent key {Key})", file, key);
            await _endpoint.InProgressRepository.RemoveAsync(file.AbsoluteFilePath).ConfigureAwait(false);
            return false;
        }

        return true;
    }

    private string? IdempotentKey(GenericFile<T> file) =>
        _endpoint.IdempotentKeyExpression is { } expression
            ? _endpoint.Context.TypeConverter.Convert(typeof(string), null, expression.Evaluate(_endpoint.CreateExchange(file))) as string
            : file.AbsoluteFilePath;

    private bool IsBatchAllowed(CancellationToken cancellationToken)
    {
        if (ForceToken.IsCancellationRequested)
        {
            return false;
        }

        if (_shutdownRunningTask == (int)ShutdownRunningTask.CompleteAllTasks)
        {
            return true;
        }

        return _shutdownRunningTask < 0 && IsPollAllowed && !cancellationToken.IsCancellationRequested;
    }

    private async Task<int> ProcessBatchAsync(Queue<(Exchange Exchange, GenericFile<T> File)> exchanges, CancellationToken cancellationToken)
    {
        var total = exchanges.Count;
        if (_endpoint.MaxMessagesPerPoll > 0 && total > _endpoint.MaxMessagesPerPoll)
        {
            total = _endpoint.MaxMessagesPerPoll;
        }

        var processed = 0;
        for (var index = 0; index < total && IsBatchAllowed(cancellationToken); index++)
        {
            var (exchange, file) = exchanges.Dequeue();
            exchange.SetProperty(ExchangePropertyKeys.BatchIndex, index);
            exchange.SetProperty(ExchangePropertyKeys.BatchSize, total);
            exchange.SetProperty(ExchangePropertyKeys.BatchComplete, index == total - 1);
            Volatile.Write(ref _pendingExchanges, total - index - 1);
            if (await ProcessExchangeAsync(exchange, file, cancellationToken).ConfigureAwait(false))
            {
                processed++;
            }
        }

        // Files not processed (limit, shutdown) are no longer in progress: the next poll takes them.
        while (exchanges.TryDequeue(out var remaining))
        {
            await _endpoint.InProgressRepository.RemoveAsync(remaining.File.AbsoluteFilePath, CancellationToken.None).ConfigureAwait(false);
        }

        Volatile.Write(ref _pendingExchanges, 0);
        return processed;
    }

    private async Task<bool> ProcessExchangeAsync(Exchange exchange, GenericFile<T> file, CancellationToken cancellationToken)
    {
        var absoluteFileName = file.AbsoluteFilePath;
        var idempotentKey = _endpoint.IsIdempotent ? IdempotentKey(file) : null;
        var strategy = _endpoint.GenericFileProcessStrategy;
        var begin = false;
        Exception? beginCause = null;
        try
        {
            begin = await strategy.BeginAsync(Operations, _endpoint, exchange, file, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            beginCause = e;
        }

        if (!begin)
        {
            await AbortAsync(exchange, file, absoluteFileName).ConfigureAwait(false);
            if (beginCause is not null)
            {
                _exceptionHandler.HandleException($"{_endpoint.EndpointUri} cannot begin processing file: {file} due to: {beginCause.Message}", exchange, beginCause);
            }
            else
            {
                Logger.LogDebug("{Uri} cannot begin processing file: {File}", _endpoint.EndpointUri, file);
            }

            return false;
        }

        // The begin strategy may have moved the file (preMove) and the read lock may have waited: refresh what changed.
        var target = exchange.GetProperty(ExchangePropertyKeys.FileExchangeFile) as GenericFile<T> ?? file;
        if (Operations.GetFile(target.AbsoluteFilePath) is not { } current)
        {
            Logger.LogDebug("Skipping file {File} as it no longer exists", target);
            await AbortAsync(exchange, target, absoluteFileName).ConfigureAwait(false);
            return false;
        }

        target.FileLength = current.FileLength;
        target.LastModified = current.LastModified;
        exchange.Message.SetHeader(CaravanHeaders.FileLength, target.FileLength);
        exchange.Message.SetHeader(CaravanHeaders.FileLastModified, target.LastModified);

        try
        {
            bool retrieved;
            Exception? cause = null;
            try
            {
                retrieved = await Operations.RetrieveFileAsync(target, exchange, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                retrieved = false;
                cause = e;
            }

            if (!retrieved)
            {
                throw cause as GenericFileOperationFailedException ??
                      new GenericFileOperationFailedException($"Cannot retrieve file: {target} from: {_endpoint.EndpointUri}", cause);
            }
        }
        catch (Exception e)
        {
            await AbortAsync(exchange, target, absoluteFileName).ConfigureAwait(false);
            _exceptionHandler.HandleException($"{_endpoint.EndpointUri} error processing file: {file} due to: {e.Message}", exchange, e);
            return false;
        }

        exchange.AddOnCompletion(new GenericFileOnCompletion<T>(
            _endpoint,
            Operations,
            strategy,
            target,
            absoluteFileName,
            idempotentKey,
            _endpoint.OnCompletionExceptionHandler ?? _exceptionHandler));
        Logger.LogDebug("About to process file: {File} using exchange: {ExchangeId}", target, exchange.ExchangeId);
        await Processor.ProcessAsync(exchange).ConfigureAwait(false);
        LogFailure(exchange);
        return true;
    }

    private async Task AbortAsync(Exchange exchange, GenericFile<T> file, string absoluteFileName)
    {
        try
        {
            await _endpoint.GenericFileProcessStrategy.AbortAsync(Operations, _endpoint, exchange, file).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            _exceptionHandler.HandleException($"{_endpoint.EndpointUri} cannot abort processing file: {file} due to: {e.Message}", exchange, e);
        }
        finally
        {
            await _endpoint.InProgressRepository.RemoveAsync(absoluteFileName, CancellationToken.None).ConfigureAwait(false);
        }
    }
}
