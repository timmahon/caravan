using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using Caravan.Aggregation;
using Caravan.Impl;
using Caravan.Spi;
using Caravan.Support;
using Microsoft.Extensions.Logging;

namespace Caravan.Processors;

/// <summary>
/// The <c>multicast</c> EIP and the engine of <c>split</c> (<see cref="SplitProcessor"/>) and <c>recipientList</c>
/// (<see cref="RecipientListProcessor"/>), like Camel's <c>MulticastProcessor</c>: each branch processes a correlated copy of the
/// input exchange, the branch results are combined with the <see cref="MulticastSettings.AggregationStrategy"/>, and the
/// aggregated result is copied into the input exchange.
/// </summary>
/// <remarks>
/// <para>
/// <b>Branches.</b> Each branch exchange gets its own unit of work (completed when the branch is done) unless
/// <see cref="MulticastSettings.ShareUnitOfWork"/>. Branch processors are built as route nodes, so they get channels and the route's
/// error handler: a branch failure is redelivered and handled at the branch level. <c>stop</c> in a branch only stops that branch.
/// </para>
/// <para>
/// <b>Failures.</b> Failed branches are aggregated like the others: the strategy decides whether the failure propagates (the default
/// strategies do) or is handled. With <see cref="MulticastSettings.StopOnException"/> no further branch is started after a failure and
/// the input exchange fails with a <see cref="CaravanExchangeException"/> ("Multicast processing failed for number N") wrapping it.
/// The input exchange keeps the branch's redelivery-exhausted flag, so enclosing error handlers do not handle the failure again.
/// </para>
/// <para>
/// <b>Parallel processing.</b> Branches run on the thread pool, at most <see cref="MulticastSettings.MaxDegreeOfParallelism"/> at a time;
/// branches are created lazily as slots free up. Aggregation is serialized and happens in branch order (results that complete early are
/// buffered) unless <see cref="MulticastSettings.Streaming"/> (completion order). The processor waits for all started branches, except
/// on <see cref="MulticastSettings.Timeout"/>, after which unfinished branches are cancelled and ignored; stopping the processor waits for them.
/// </para>
/// <para>
/// <b>Cancellation.</b> When the input exchange's <see cref="Exchange.CancellationToken"/> is cancelled no further branch is started,
/// running branches are awaited and the input exchange fails with an <see cref="OperationCanceledException"/>.
/// </para>
/// </remarks>
public class MulticastProcessor : ServiceSupport, INavigableProcessor
{
    private readonly ConcurrentDictionary<long, Task> _running = new();
    private long _sequence;

    /// <summary>Creates the processor.</summary>
    /// <param name="processors">The branches: each processes its own copy of the input exchange.</param>
    /// <param name="settings">The options.</param>
    public MulticastProcessor(IReadOnlyList<IProcessor> processors, MulticastSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.MaxDegreeOfParallelism <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(settings), "MaxDegreeOfParallelism must be positive");
        }

        if (settings.Timeout is { } timeout && timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(settings), "Timeout must be positive");
        }

        Processors = processors;
        Settings = settings;
    }

    /// <summary>The branch processors.</summary>
    public IReadOnlyList<IProcessor> Processors { get; }

    /// <summary>The options.</summary>
    public MulticastSettings Settings { get; }

    /// <inheritdoc />
    public IReadOnlyList<IProcessor> Next => Processors;

    /// <summary>The number of branches running in the background (parallel branches, including those abandoned after a timeout).</summary>
    public int RunningCount => _running.Count;

    /// <inheritdoc />
    public async ValueTask ProcessAsync(Exchange exchange)
    {
        var strategy = Settings.AggregationStrategy is UseOriginalAggregationStrategy { Original: null } useOriginal
            ? useOriginal.NewInstance(exchange)
            : Settings.AggregationStrategy;
        var run = new Run(this, exchange, strategy);
        if (Settings.ParallelProcessing)
        {
            await run.ProcessParallelAsync().ConfigureAwait(false);
        }
        else
        {
            await run.ProcessSequentialAsync().ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public override string ToString() => $"Multicast[{string.Join(", ", Processors)}]";

    /// <summary>Creates the branches for <paramref name="original"/>, lazily. The default creates one per processor.</summary>
    private protected virtual async IAsyncEnumerable<MulticastBranch> CreateBranchesAsync(Exchange original, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await Task.CompletedTask.ConfigureAwait(false);
        foreach (var processor in Processors)
        {
            yield return new MulticastBranch(processor, original.CreateCorrelatedCopy(), Processors.Count);
        }
    }

    /// <summary>Sets the EIP's properties on a branch exchange before it is processed.</summary>
    private protected virtual void UpdateNewExchange(Exchange exchange, int index, bool hasNext, MulticastBranch branch)
    {
        exchange.SetProperty(ExchangePropertyKeys.MulticastIndex, index);
        exchange.SetProperty(ExchangePropertyKeys.MulticastComplete, !hasNext);
    }

    /// <summary>Waits for branches still running in the background (e.g. abandoned after a timeout).</summary>
    protected override async Task DoStopAsync(CancellationToken cancellationToken)
    {
        while (!_running.IsEmpty)
        {
            await Task.WhenAll(_running.Values).WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static bool IsStopFailure(Exchange exchange) =>
        exchange.Exception is not null || exchange.IsRollbackOnly || exchange.IsRollbackOnlyLast || exchange.ErrorHandlerHandled;

    private void Prepare(Exchange original, MulticastBranch branch, int index, bool hasNext)
    {
        var exchange = branch.Exchange;
        UpdateNewExchange(exchange, index, hasNext, branch);
        if (Settings.ShareUnitOfWork)
        {
            exchange.UnitOfWork = original.UnitOfWork;
            exchange.IsSharedUnitOfWorkChild = true;
        }

        try
        {
            Settings.OnPrepare?.Invoke(exchange);
        }
        catch (Exception e)
        {
            exchange.Exception = e;
        }
    }

    private async Task RunBranchAsync(MulticastBranch branch)
    {
        var exchange = branch.Exchange;
        if (exchange.Exception is null)
        {
            var unitOfWork = Settings.ShareUnitOfWork ? null : new DefaultUnitOfWork(exchange);
            if (unitOfWork is not null)
            {
                exchange.UnitOfWork = unitOfWork;
            }

            try
            {
                await branch.Processor.ProcessAsync(exchange).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                exchange.Exception = e;
            }
            finally
            {
                if (unitOfWork is not null)
                {
                    await unitOfWork.DoneAsync(exchange).ConfigureAwait(false);
                }
            }
        }

        // The branch is done: it no longer takes part in the unit of work, and stop only applied to it.
        exchange.UnitOfWork = null;
        exchange.IsSharedUnitOfWorkChild = false;
        exchange.IsRouteStop = false;
    }

    private void Track(Task task)
    {
        var id = Interlocked.Increment(ref _sequence);
        _running.TryAdd(id, task);
        task.ContinueWith(_ => _running.TryRemove(id, out Task? _), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    /// <summary>The state of one input exchange going through the processor.</summary>
    private sealed class Run(MulticastProcessor owner, Exchange original, IAggregationStrategy strategy)
    {
        private readonly MulticastSettings _settings = owner.Settings;
        private readonly Lock _lock = new();
        private readonly SortedDictionary<int, Exchange> _completed = [];
        private readonly HashSet<int> _aggregated = [];
        private readonly TaskCompletionSource _allDone = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private CancellationTokenSource? _timeoutSource;
        private Exchange? _result;
        private Exchange? _sharedFailure;
        private Exception? _aggregationError;
        private Exchange? _stoppedBy;
        private int _stoppedIndex;
        private int _nextIndex;
        private int _running;
        private bool _dispatchDone;
        private bool _finished;

        private bool Stopping
        {
            get
            {
                lock (_lock)
                {
                    return _stoppedBy is not null || _aggregationError is not null;
                }
            }
        }

        public async Task ProcessSequentialAsync()
        {
            var token = original.CancellationToken;
            var index = 0;
            var branches = owner.CreateBranchesAsync(original, token).GetAsyncEnumerator(token);
            await using (branches.ConfigureAwait(false))
            {
                var hasNext = await branches.MoveNextAsync().ConfigureAwait(false);
                while (hasNext)
                {
                    var branch = branches.Current;
                    hasNext = await branches.MoveNextAsync().ConfigureAwait(false);
                    token.ThrowIfCancellationRequested();
                    owner.Prepare(original, branch, index, hasNext);
                    await owner.RunBranchAsync(branch).ConfigureAwait(false);
                    if (_settings.StopOnException && IsStopFailure(branch.Exchange))
                    {
                        _stoppedBy = branch.Exchange;
                        _stoppedIndex = index;
                        break;
                    }

                    if (!Aggregate(branch.Exchange))
                    {
                        break;
                    }

                    index++;
                }
            }

            Complete();
        }

        public async Task ProcessParallelAsync()
        {
            var timeout = _settings.Timeout;
            var timeoutSource = timeout is { } t ? new CancellationTokenSource(t, original.Context.TimeProvider) : null;
            _timeoutSource = timeoutSource;
            var linkedSource = timeoutSource is null ? null : CancellationTokenSource.CreateLinkedTokenSource(original.CancellationToken, timeoutSource.Token);
            var token = linkedSource?.Token ?? original.CancellationToken;
            var throttle = new SemaphoreSlim(_settings.MaxDegreeOfParallelism, _settings.MaxDegreeOfParallelism);
            var index = 0;
            ExceptionDispatchInfo? dispatchError = null;
            try
            {
                var branches = owner.CreateBranchesAsync(original, token).GetAsyncEnumerator(token);
                await using (branches.ConfigureAwait(false))
                {
                    var hasNext = await branches.MoveNextAsync().ConfigureAwait(false);
                    while (hasNext && !Stopping)
                    {
                        await throttle.WaitAsync(token).ConfigureAwait(false);

                        // WaitAsync can succeed after cancellation (a slot freed while the cancellation is processed): check again.
                        if (Stopping || token.IsCancellationRequested)
                        {
                            throttle.Release();
                            break;
                        }

                        var branch = branches.Current;
                        hasNext = await branches.MoveNextAsync().ConfigureAwait(false);
                        owner.Prepare(original, branch, index, hasNext);
                        branch.Exchange.CancellationToken = token;
                        Start(index, branch, throttle);
                        index++;
                    }
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                // Timed out or cancelled: start no more branches.
            }
            catch (Exception e)
            {
                // Creating the branches failed: fail once the started branches are done.
                dispatchError = ExceptionDispatchInfo.Capture(e);
            }

            lock (_lock)
            {
                _dispatchDone = true;
                if (_running == 0)
                {
                    _allDone.TrySetResult();
                }
            }

            bool timedOut;
            if (timeoutSource is not null)
            {
                var elapsed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                using (timeoutSource.Token.Register(() => elapsed.TrySetResult()))
                {
                    await Task.WhenAny(_allDone.Task, elapsed.Task).ConfigureAwait(false);
                }
            }
            else
            {
                await _allDone.Task.ConfigureAwait(false);
            }

            bool abandoned;
            lock (_lock)
            {
                _finished = true;
                abandoned = !_allDone.Task.IsCompleted;
                timedOut = timeoutSource?.IsCancellationRequested == true;
                if (timedOut)
                {
                    OnTimeout(index, timeout!.Value);
                }
            }

            if (!abandoned)
            {
                // Abandoned branches may still use the token and the throttle: only dispose when every branch is done.
                linkedSource?.Dispose();
                timeoutSource?.Dispose();
                throttle.Dispose();
            }

            dispatchError?.Throw();
            if (!timedOut)
            {
                original.CancellationToken.ThrowIfCancellationRequested();
            }

            Complete();
        }

        private void Start(int index, MulticastBranch branch, SemaphoreSlim throttle)
        {
            lock (_lock)
            {
                _running++;
            }

            var task = Task.Run(async () =>
            {
                try
                {
                    await owner.RunBranchAsync(branch).ConfigureAwait(false);
                    OnBranchCompleted(index, branch.Exchange);
                }
                finally
                {
                    // Released after aggregation, so a failure stops the dispatch before the next branch starts.
                    throttle.Release();
                    lock (_lock)
                    {
                        _running--;
                        if (_running == 0 && _dispatchDone)
                        {
                            _allDone.TrySetResult();
                        }
                    }
                }
            });
            owner.Track(task);
        }

        private void OnBranchCompleted(int index, Exchange exchange)
        {
            lock (_lock)
            {
                // Results that arrive after the deadline are ignored, even before the timeout is processed.
                if (_finished || _stoppedBy is not null || _aggregationError is not null || _timeoutSource?.IsCancellationRequested == true)
                {
                    return;
                }

                if (_settings.StopOnException && IsStopFailure(exchange))
                {
                    _stoppedBy = exchange;
                    _stoppedIndex = index;
                    return;
                }

                if (!_settings.Streaming)
                {
                    // Aggregate in branch order: buffer results that complete before earlier ones.
                    _completed[index] = exchange;
                    while (_completed.Remove(_nextIndex, out var next))
                    {
                        _aggregated.Add(_nextIndex);
                        _nextIndex++;
                        if (!Aggregate(next))
                        {
                            return;
                        }
                    }

                    return;
                }

                _aggregated.Add(index);
                if (!_settings.ParallelAggregate)
                {
                    Aggregate(exchange);
                    return;
                }
            }

            AggregateConcurrently(exchange);
        }

        private void OnTimeout(int total, TimeSpan timeout)
        {
            if (_stoppedBy is not null || _aggregationError is not null)
            {
                return;
            }

            original.Context.LoggerFactory.CreateLogger<MulticastProcessor>().LogDebug(
                "Parallel processing of exchange {ExchangeId} timed out after {Timeout}; {Completed} of {Total} branches completed",
                original.ExchangeId,
                timeout,
                _aggregated.Count + _completed.Count,
                total);
            for (var i = 0; i < total; i++)
            {
                if (_aggregated.Contains(i))
                {
                    continue;
                }

                if (_completed.Remove(i, out var completed))
                {
                    _aggregated.Add(i);
                    if (!Aggregate(completed))
                    {
                        return;
                    }

                    continue;
                }

                try
                {
                    strategy.OnTimeout(_result ?? original, i, total, timeout);
                }
                catch (Exception e)
                {
                    _aggregationError = e;
                    return;
                }
            }
        }

        private bool Aggregate(Exchange exchange)
        {
            if (_settings.ShareUnitOfWork && exchange.Exception is not null)
            {
                _sharedFailure ??= exchange;
            }

            try
            {
                _result = strategy.Aggregate(_result, exchange, original);
                return true;
            }
            catch (Exception e)
            {
                _aggregationError = e;
                return false;
            }
        }

        private void AggregateConcurrently(Exchange exchange)
        {
            try
            {
                var result = strategy.Aggregate(Volatile.Read(ref _result), exchange, original);
                Volatile.Write(ref _result, result);
            }
            catch (Exception e)
            {
                lock (_lock)
                {
                    _aggregationError ??= e;
                }
            }
        }

        private void Complete()
        {
            if (_aggregationError is { } aggregationError)
            {
                original.Exception = aggregationError;
            }
            else if (_stoppedBy is { } stoppedBy)
            {
                if (stoppedBy.Exception is { } exception)
                {
                    original.Exception = new CaravanExchangeException($"Multicast processing failed for number {_stoppedIndex}", stoppedBy, exception);
                    original.IsRedeliveryExhausted = stoppedBy.IsRedeliveryExhausted;
                }
                else
                {
                    // Stopped because an error handler handled the failure (or it was rolled back): that branch is the result.
                    CopyResults(stoppedBy, stopped: true);
                }
            }
            else if (Volatile.Read(ref _result) is { } result)
            {
                strategy.OnCompletion(result, original);
                CopyResults(result, stopped: false);
            }

            if (_settings.ShareUnitOfWork)
            {
                if (original.Exception is null && _sharedFailure?.Exception is { } sharedFailure)
                {
                    original.Exception = sharedFailure;
                }

                if (original.Exception is not null)
                {
                    // The branch was redelivered by its error handler but not handled: the input exchange's error handler handles it.
                    original.IsRedeliveryExhausted = false;
                    original.SuppressRedelivery = true;
                }
            }
        }

        private void CopyResults(Exchange result, bool stopped)
        {
            if (ReferenceEquals(result, original))
            {
                return;
            }

            original.Message = result.Message.Copy(original);
            foreach (var (key, value) in result.Properties)
            {
                if (key != ExchangePropertyKeys.CorrelationId)
                {
                    original.Properties[key] = value;
                }
            }

            original.Exception = result.Exception;
            original.IsRedeliveryExhausted = result.IsRedeliveryExhausted;
            original.IsRollbackOnly = result.IsRollbackOnly;
            original.IsRollbackOnlyLast = result.IsRollbackOnlyLast;
            if (stopped)
            {
                original.ErrorHandlerHandled = result.ErrorHandlerHandled;
            }
        }
    }
}
