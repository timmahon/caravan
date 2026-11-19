using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;
using Caravan.Aggregation;
using Caravan.Spi;
using Caravan.Support;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Caravan.Processors;

/// <summary>
/// The <c>aggregate</c> EIP (Camel's <c>AggregateProcessor</c>): exchanges are grouped by correlation key and combined with an
/// <see cref="IAggregationStrategy"/>; when a group completes, the aggregated exchange is sent through <see cref="Processor"/>
/// (the EIP's nested block) as a new exchange, detached from the exchanges that were aggregated.
/// </summary>
/// <remarks>
/// <para>
/// <b>Aggregation.</b> Each incoming exchange is copied (a correlated copy, see <see cref="Exchange.CreateCorrelatedCopy"/>), the
/// group so far is read from the <see cref="Repository"/>, the copy is aggregated into it and the result is stored back, unless the
/// group completed. The incoming exchange then continues unchanged. Calls are serialized by a lock, or, with
/// <see cref="AggregateSettings.OptimisticLocking"/>, run concurrently and retried when the repository detects a conflict.
/// </para>
/// <para>
/// <b>Completion.</b> Checked after aggregating (or on the incoming exchange first with <see cref="AggregateSettings.EagerCheckCompletion"/>),
/// in Camel's order: batch consumer, <see cref="ExchangePropertyKeys.AggregationCompleteCurrentGroup"/>, predicate, size. Timeouts
/// (per group, restarted by each exchange) are checked every <see cref="AggregateSettings.CompletionTimeoutCheckerInterval"/>; the
/// interval completes all groups periodically. A strategy with <see cref="IAggregationStrategy.CanPreComplete"/> can complete the
/// group before the new exchange is added to it. The completed exchange gets <see cref="ExchangePropertyKeys.AggregatedSize"/>,
/// <see cref="ExchangePropertyKeys.AggregatedCompletedBy"/>, <see cref="ExchangePropertyKeys.AggregatedCorrelationKey"/> (and
/// <see cref="ExchangePropertyKeys.AggregatedTimeout"/> when completed by timeout); the strategy's
/// <see cref="IAggregationStrategy.OnCompletion"/> is called just before it is sent.
/// </para>
/// <para>
/// <b>Sending.</b> Without <see cref="AggregateSettings.ParallelProcessing"/>, a completed exchange is sent on the thread that completed
/// it: the incoming exchange (before it continues), the timeout checker or the interval task, or the caller of a forced completion.
/// With it, completed exchanges are sent on the thread pool. Each one gets its own unit of work and is tracked in the inflight
/// repository; when it is processed successfully it is confirmed in the repository (see <see cref="IRecoverableAggregationRepository"/>).
/// </para>
/// <para>
/// <b>Shutdown.</b> As a route service implementing <see cref="IShutdownAware"/>, the processor reports the exchanges being sent (and,
/// with <see cref="AggregateSettings.CompleteAllOnStop"/>, the stored groups) as pending, so a graceful stop waits for them; with
/// <see cref="AggregateSettings.ForceCompletionOnStop"/> it completes all groups when the route is being stopped. Stopping waits for the
/// exchanges being sent.
/// </para>
/// </remarks>
public sealed class AggregateProcessor : ServiceSupport, INavigableProcessor, IShutdownAware
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly Lock _stateLock = new();
    private readonly Dictionary<string, TimeoutEntry> _timeouts = new(StringComparer.Ordinal);
    private readonly HashSet<string> _closedKeys = new(StringComparer.Ordinal);
    private readonly Queue<string> _closedOrder = new();
    private readonly List<string> _batchKeys = [];
    private readonly ConcurrentDictionary<string, byte> _inProgress = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, int> _redeliveries = new(StringComparer.Ordinal);
    private readonly List<Task> _loopTasks = [];
    private readonly DetachedExchangeSender _sender;
    private readonly SemaphoreSlim? _slots;
    private readonly bool _preCompletion;
    private readonly IPredicate? _completionPredicate;
    private int _batchCounter;
    private CancellationTokenSource? _loops;
    private Task? _forceOnStop;
    private IProducerTemplate? _deadLetterTemplate;
    private ILogger _logger = NullLogger.Instance;

    /// <summary>Creates the processor.</summary>
    /// <param name="context">The context.</param>
    /// <param name="processor">Processes the completed exchanges (the EIP's nested block).</param>
    /// <param name="correlationExpression">Computes the correlation key; null or blank keys are invalid.</param>
    /// <param name="aggregationStrategy">Combines the exchanges of a group.</param>
    /// <param name="settings">The options.</param>
    /// <param name="routeId">The id of the route (completed exchanges are tracked as inflight in it).</param>
    /// <exception cref="ArgumentException">The options are inconsistent (e.g. both a completion timeout and interval).</exception>
    /// <exception cref="InvalidOperationException">No completion condition is configured.</exception>
    public AggregateProcessor(
        ICaravanContext context,
        IProcessor processor,
        IExpression correlationExpression,
        IAggregationStrategy aggregationStrategy,
        AggregateSettings settings,
        string routeId)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(processor);
        ArgumentNullException.ThrowIfNull(correlationExpression);
        ArgumentNullException.ThrowIfNull(aggregationStrategy);
        ArgumentNullException.ThrowIfNull(settings);
        Context = context;
        Processor = processor;
        CorrelationExpression = correlationExpression;
        AggregationStrategy = aggregationStrategy;
        Settings = settings;
        RouteId = routeId;
        Repository = settings.AggregationRepository ?? new MemoryAggregationRepository(settings.OptimisticLocking);
        _preCompletion = aggregationStrategy.CanPreComplete;

        // As in Camel, a strategy that is also a predicate is the completion predicate when none is configured.
        _completionPredicate = settings.CompletionPredicate ?? aggregationStrategy as IPredicate;
        Validate();
        _sender = new DetachedExchangeSender(processor, routeId, "aggregated");
        _slots = settings.ParallelProcessing ? new SemaphoreSlim(settings.MaxDegreeOfParallelism, settings.MaxDegreeOfParallelism) : null;
    }

    /// <summary>The context.</summary>
    public ICaravanContext Context { get; }

    /// <summary>Processes the completed exchanges.</summary>
    public IProcessor Processor { get; }

    /// <summary>Computes the correlation key.</summary>
    public IExpression CorrelationExpression { get; }

    /// <summary>Combines the exchanges of a group.</summary>
    public IAggregationStrategy AggregationStrategy { get; }

    /// <summary>The options.</summary>
    public AggregateSettings Settings { get; }

    /// <summary>Stores the groups being aggregated.</summary>
    public IAggregationRepository Repository { get; }

    /// <summary>The id of the route.</summary>
    public string RouteId { get; }

    /// <inheritdoc />
    public IReadOnlyList<IProcessor> Next => [Processor];

    /// <summary>The number of completed exchanges being sent (or waiting to be sent).</summary>
    public int InProgressCompleteExchanges => _inProgress.Count;

    /// <summary>The number of closed correlation keys remembered (<see cref="AggregateSettings.CloseCorrelationKeyOnCompletion"/>).</summary>
    public int ClosedCorrelationKeysCount
    {
        get
        {
            lock (_stateLock)
            {
                return _closedKeys.Count;
            }
        }
    }

    /// <inheritdoc />
    public int PendingExchangesSize
    {
        get
        {
            var pending = _sender.Pending + (_forceOnStop is { IsCompleted: false } ? 1 : 0);
            if (Settings.CompleteAllOnStop && IsRunAllowed)
            {
                var keys = Repository.GetKeysAsync();
                pending += keys.IsCompletedSuccessfully ? keys.Result.Count : keys.AsTask().GetAwaiter().GetResult().Count;
            }

            return pending;
        }
    }

    /// <inheritdoc />
    public async ValueTask ProcessAsync(Exchange exchange)
    {
        var message = exchange.Message;
        if (message.GetHeader(ExchangePropertyKeys.AggregationCompleteAllGroups, false))
        {
            // Completes all groups; the exchange itself is not aggregated.
            message.RemoveHeader(ExchangePropertyKeys.AggregationCompleteAllGroups);
            await ForceCompletionOfAllGroupsAsync().ConfigureAwait(false);
            return;
        }

        var key = EvaluateKey(exchange);
        if (key is null)
        {
            if (Settings.IgnoreInvalidCorrelationKeys)
            {
                _logger.LogDebug("Invalid correlation key; exchange {ExchangeId} is ignored", exchange.ExchangeId);
                return;
            }

            throw new CaravanExchangeException("Invalid correlation key", exchange);
        }

        if (IsClosed(key))
        {
            throw new ClosedCorrelationKeyException(key, exchange);
        }

        var completed = new List<Exchange>();
        if (Settings.OptimisticLocking)
        {
            await AggregateOptimisticallyAsync(key, exchange, completed).ConfigureAwait(false);
        }
        else
        {
            ExceptionDispatchInfo? failure = null;
            await _lock.WaitAsync(exchange.CancellationToken).ConfigureAwait(false);
            try
            {
                await DoAggregationAsync(key, exchange.CreateCorrelatedCopy(), exchange, completed).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                failure = ExceptionDispatchInfo.Capture(e);
            }
            finally
            {
                _lock.Release();
            }

            // Groups completed before a failure (e.g. other groups completed by a new correlation group) are sent anyway.
            await SubmitAsync(completed, exchange).ConfigureAwait(false);
            failure?.Throw();
        }

        if (message.GetHeader(ExchangePropertyKeys.AggregationCompleteAllGroupsInclusive, false))
        {
            message.RemoveHeader(ExchangePropertyKeys.AggregationCompleteAllGroupsInclusive);
            await ForceCompletionOfAllGroupsAsync().ConfigureAwait(false);
        }
    }

    /// <summary>Completes the group of <paramref name="key"/> now (completed by <c>force</c>) and sends it.</summary>
    /// <param name="key">The correlation key.</param>
    /// <returns>The number of groups completed (0 when there is no such group).</returns>
    public async Task<int> ForceCompletionOfGroupAsync(string key)
    {
        var completed = new List<Exchange>();
        bool found;
        await LockAsync().ConfigureAwait(false);
        try
        {
            found = await CompleteGroupAsync(key, AggregationCompletedBy.Force, fromTimeout: false, timeout: null, completed).ConfigureAwait(false);
        }
        finally
        {
            Unlock();
        }

        await SubmitAsync(completed, null).ConfigureAwait(false);
        return found ? 1 : 0;
    }

    /// <summary>Completes all groups now (completed by <c>force</c>) and sends them.</summary>
    /// <returns>The number of groups completed.</returns>
    public async Task<int> ForceCompletionOfAllGroupsAsync()
    {
        var completed = new List<Exchange>();
        int count;
        await LockAsync().ConfigureAwait(false);
        try
        {
            count = await ForceCompletionOfAllGroupsCoreAsync(completed, exceptKey: null).ConfigureAwait(false);
        }
        finally
        {
            Unlock();
        }

        await SubmitAsync(completed, null).ConfigureAwait(false);
        return count;
    }

    /// <summary>Discards the group of <paramref name="key"/> without sending it.</summary>
    /// <param name="key">The correlation key.</param>
    /// <returns>The number of groups discarded.</returns>
    public async Task<int> ForceDiscardingOfGroupAsync(string key)
    {
        await LockAsync().ConfigureAwait(false);
        try
        {
            return await DiscardGroupAsync(key).ConfigureAwait(false) ? 1 : 0;
        }
        finally
        {
            Unlock();
        }
    }

    /// <summary>Discards all groups without sending them.</summary>
    /// <returns>The number of groups discarded.</returns>
    public async Task<int> ForceDiscardingOfAllGroupsAsync()
    {
        await LockAsync().ConfigureAwait(false);
        try
        {
            var count = 0;
            foreach (var key in await Repository.GetKeysAsync().ConfigureAwait(false))
            {
                if (await DiscardGroupAsync(key).ConfigureAwait(false))
                {
                    count++;
                }
            }

            return count;
        }
        finally
        {
            Unlock();
        }
    }

    /// <summary>Forgets the closed correlation keys, so exchanges for them start new groups again.</summary>
    public void ClearClosedCorrelationKeys()
    {
        lock (_stateLock)
        {
            _closedKeys.Clear();
            _closedOrder.Clear();
        }
    }

    /// <inheritdoc />
    public bool DeferShutdown(ShutdownRunningTask runningTask) => false;

    /// <summary>
    /// When the route is being stopped (not suspended, not forced) and <see cref="AggregateSettings.ForceCompletionOnStop"/> is set,
    /// completes all groups; the exchanges being sent are reported in <see cref="PendingExchangesSize"/>.
    /// </summary>
    /// <param name="runningTask">The route's <see cref="ShutdownRunningTask"/>.</param>
    /// <param name="suspendOnly">True when the route is being suspended.</param>
    /// <param name="forced">True when the shutdown timed out.</param>
    public void PrepareShutdown(ShutdownRunningTask runningTask, bool suspendOnly, bool forced)
    {
        if (forced || suspendOnly || !Settings.ForceCompletionOnStop || !IsStarted)
        {
            return;
        }

        lock (_stateLock)
        {
            _forceOnStop ??= Task.Run(ForceCompletionOnStopAsync);
        }
    }

    /// <inheritdoc />
    public override string ToString() => $"Aggregate[{CorrelationExpression} -> {Processor}]";

    /// <summary>Starts the repository and the timeout checker, interval and recovery tasks.</summary>
    protected override async Task DoStartAsync(CancellationToken cancellationToken)
    {
        _logger = Context.LoggerFactory.CreateLogger<AggregateProcessor>();
        _forceOnStop = null;
        await StartServiceAsync(Repository, cancellationToken).ConfigureAwait(false);
        var recoverable = Repository as IRecoverableAggregationRepository;
        if (recoverable is { MaximumRedeliveries: > 0, DeadLetterUri: null })
        {
            throw new ArgumentException("Option maximumRedeliveries requires deadLetterUri to be set on the aggregation repository");
        }

        if (recoverable is { UseRecovery: true, DeadLetterUri: not null })
        {
            _deadLetterTemplate = Context.CreateProducerTemplate();
        }

        _loops = new CancellationTokenSource();
        var token = _loops.Token;
        if (HasCompletionTimeout)
        {
            await RestoreTimeoutsAsync(cancellationToken).ConfigureAwait(false);

            // Started inline so the first delay is scheduled before StartAsync returns.
            _loopTasks.Add(RunLoopAsync(Settings.CompletionTimeoutCheckerInterval, fixedRate: false, CheckTimeoutsAsync, token));
        }

        if (Settings.CompletionInterval is { } interval)
        {
            _loopTasks.Add(RunLoopAsync(interval, fixedRate: true, CompleteByIntervalAsync, token));
        }

        if (recoverable is { UseRecovery: true })
        {
            _loopTasks.Add(RunLoopAsync(recoverable.RecoveryInterval, fixedRate: false, RecoverAsync, token));
        }

        Settings.AggregateController?.OnStart(this);
    }

    /// <summary>
    /// Waits for a completion forced on stop, stops the background tasks, waits for the exchanges being sent, then stops the
    /// repository (a <see cref="MemoryAggregationRepository"/> forgets its groups).
    /// </summary>
    protected override async Task DoStopAsync(CancellationToken cancellationToken)
    {
        Settings.AggregateController?.OnStop(this);
        if (_forceOnStop is { } forceOnStop)
        {
            await forceOnStop.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        if (_loops is { } loops)
        {
            await loops.CancelAsync().ConfigureAwait(false);
            await Task.WhenAll(_loopTasks).WaitAsync(cancellationToken).ConfigureAwait(false);
            _loopTasks.Clear();
            loops.Dispose();
            _loops = null;
        }

        await _sender.DrainAsync(cancellationToken).ConfigureAwait(false);
        if (_deadLetterTemplate is { } template)
        {
            await template.DisposeAsync().ConfigureAwait(false);
            _deadLetterTemplate = null;
        }

        await StopServiceAsync(Repository, cancellationToken).ConfigureAwait(false);
        lock (_stateLock)
        {
            _timeouts.Clear();
            _closedKeys.Clear();
            _closedOrder.Clear();
            _batchKeys.Clear();
            _batchCounter = 0;
            _forceOnStop = null;
        }

        _redeliveries.Clear();
    }

    private bool HasCompletionTimeout => Settings.CompletionTimeout is not null || Settings.CompletionTimeoutExpression is not null;

    private void Validate()
    {
        if (Settings.CompletionSize < 0)
        {
            throw new ArgumentException("completionSize must not be negative");
        }

        if (Settings.CompletionTimeout is { } timeout && timeout <= TimeSpan.Zero)
        {
            throw new ArgumentException("completionTimeout must be positive");
        }

        if (Settings.CompletionInterval is { } interval && interval <= TimeSpan.Zero)
        {
            throw new ArgumentException("completionInterval must be positive");
        }

        if (Settings.CompletionTimeoutCheckerInterval <= TimeSpan.Zero)
        {
            throw new ArgumentException("completionTimeoutCheckerInterval must be positive");
        }

        if (Settings.CompletionInterval is not null && Settings.CompletionTimeout is not null)
        {
            throw new ArgumentException("Only one of completionInterval or completionTimeout can be used, not both.");
        }

        if (Settings.MaxDegreeOfParallelism <= 0)
        {
            throw new ArgumentException("maxDegreeOfParallelism must be positive");
        }

        if (Settings.CloseCorrelationKeyOnCompletion is < 0)
        {
            throw new ArgumentException("closeCorrelationKeyOnCompletion must not be negative");
        }

        if (Settings.OptimisticLocking && Repository is not IOptimisticLockingAggregationRepository)
        {
            throw new ArgumentException(
                $"Optimistic locking cannot be enabled without using an aggregation repository that implements {nameof(IOptimisticLockingAggregationRepository)}");
        }

        if (!_preCompletion &&
            Settings is { CompletionSize: 0, CompletionSizeExpression: null, CompletionTimeout: null, CompletionTimeoutExpression: null, CompletionInterval: null, CompletionFromBatchConsumer: false, CompletionOnNewCorrelationGroup: false } &&
            _completionPredicate is null)
        {
            throw new InvalidOperationException(
                "At least one of the completions options [completionTimeout, completionInterval, completionSize, completionPredicate, completionFromBatchConsumer, completionOnNewCorrelationGroup] must be set");
        }
    }

    private string? EvaluateKey(Exchange exchange)
    {
        var value = CorrelationExpression.Evaluate(exchange);
        var text = value is null ? null : ObjectHelper.ToText(exchange, value) ?? value.ToString();
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    private bool IsClosed(string key)
    {
        lock (_stateLock)
        {
            return _closedKeys.Contains(key);
        }
    }

    private void CloseKey(string key, int capacity)
    {
        lock (_stateLock)
        {
            if (!_closedKeys.Add(key))
            {
                return;
            }

            _closedOrder.Enqueue(key);
            while (capacity > 0 && _closedKeys.Count > capacity)
            {
                _closedKeys.Remove(_closedOrder.Dequeue());
            }
        }
    }

    private Task LockAsync() => Settings.OptimisticLocking ? Task.CompletedTask : _lock.WaitAsync();

    private void Unlock()
    {
        if (!Settings.OptimisticLocking)
        {
            _lock.Release();
        }
    }

    private async Task AggregateOptimisticallyAsync(string key, Exchange exchange, List<Exchange> completed)
    {
        var policy = Settings.OptimisticLockRetryPolicy;
        var attempt = 0;
        while (true)
        {
            attempt++;
            try
            {
                await DoAggregationAsync(key, exchange.CreateCorrelatedCopy(), exchange, completed).ConfigureAwait(false);
            }
            catch (Exception e) when (e is not OptimisticLockingException)
            {
                await SubmitAsync(completed, exchange).ConfigureAwait(false);
                throw;
            }
            catch (OptimisticLockingException)
            {
                // Groups completed before the conflict (e.g. other groups completed by a new correlation group) are sent anyway.
                await SubmitAsync(completed, exchange).ConfigureAwait(false);
                completed.Clear();
                if (!policy.ShouldRetry(attempt))
                {
                    throw new CaravanExchangeException(
                        $"Exhausted optimistic locking retry attempts, tried {attempt} times",
                        exchange,
                        new OptimisticLockingException());
                }

                _logger.LogTrace("Optimistic locking conflict for correlation key {Key} (attempt {Attempt}); retrying", key, attempt);
                await policy.DelayAsync(attempt, Context.TimeProvider, exchange.CancellationToken).ConfigureAwait(false);
                continue;
            }

            await SubmitAsync(completed, exchange).ConfigureAwait(false);
            return;
        }
    }

    /// <summary>Aggregates <paramref name="newExchange"/> into its group; completed groups are added to <paramref name="completed"/>.</summary>
    private async Task DoAggregationAsync(string key, Exchange newExchange, Exchange input, List<Exchange> completed)
    {
        if (IsClosed(key))
        {
            throw new ClosedCorrelationKeyException(key, input);
        }

        var original = await Repository.GetAsync(Context, key, input.CancellationToken).ConfigureAwait(false);

        // With optimistic locking the strategy works on a copy, so the exchange read keeps what identifies its version.
        var old = original is not null && Settings.OptimisticLocking ? original.Copy() : original;
        var size = old is null ? 1 : old.GetProperty(ExchangePropertyKeys.AggregatedSize, 0) + 1;
        string? complete = null;
        if (_preCompletion)
        {
            newExchange.SetProperty(ExchangePropertyKeys.AggregatedSize, size);
            try
            {
                complete = AggregationStrategy.PreComplete(old, newExchange) ? AggregationCompletedBy.Strategy : null;
            }
            catch (Exception e)
            {
                throw new CaravanExchangeException("Error occurred during preComplete", input, e);
            }
            finally
            {
                newExchange.RemoveProperty(ExchangePropertyKeys.AggregatedSize);
            }

            if (complete is not null)
            {
                // The group completes before the new exchange, which starts a new group.
                if (original is not null && old is not null)
                {
                    await CompleteAsync(complete, key, original, old, aggregateFailed: false, completed).ConfigureAwait(false);
                }

                complete = null;
                original = null;
                old = null;
                size = 1;
            }

            TrackTimeout(key, newExchange);
        }
        else if (Settings.EagerCheckCompletion)
        {
            newExchange.SetProperty(ExchangePropertyKeys.AggregatedSize, size);
            try
            {
                complete = IsCompleted(key, newExchange, newExchange);
            }
            finally
            {
                newExchange.RemoveProperty(ExchangePropertyKeys.AggregatedSize);
            }

            if (complete is null)
            {
                TrackTimeout(key, newExchange);
            }
        }

        var aggregateFailed = false;
        Exchange? answer;
        try
        {
            answer = AggregationStrategy.Aggregate(old, newExchange);
        }
        catch (Exception e)
        {
            if (!Settings.DiscardOnAggregationFailure)
            {
                throw new CaravanExchangeException("Error occurred during aggregation", input, e);
            }

            _logger.LogDebug(e, "Aggregation for correlation key {Key} failed; discarding the group", key);
            aggregateFailed = true;
            complete = AggregationCompletedBy.Strategy;
            answer = old;
            if (answer is null)
            {
                // The first exchange of the group failed: nothing to discard but the exchange itself.
                return;
            }
        }

        if (answer is null)
        {
            throw new CaravanExchangeException($"AggregationStrategy {AggregationStrategy} returned null which is not allowed", input);
        }

        var forceAll = false;
        if (!aggregateFailed && answer.GetProperty(ExchangePropertyKeys.AggregationCompleteAllGroups, false))
        {
            answer.RemoveProperty(ExchangePropertyKeys.AggregationCompleteAllGroups);
            forceAll = true;
        }

        if (forceAll || (Settings.CompletionOnNewCorrelationGroup && original is null))
        {
            await ForceCompletionOfAllGroupsCoreAsync(completed, exceptKey: key).ConfigureAwait(false);
        }

        answer.SetProperty(ExchangePropertyKeys.AggregatedSize, size);
        if (complete is null && !Settings.EagerCheckCompletion)
        {
            complete = IsCompleted(key, answer, newExchange);
            if (complete is null)
            {
                TrackTimeout(key, newExchange);
            }
        }

        if (complete is null && forceAll)
        {
            complete = AggregationCompletedBy.Force;
        }

        if (complete is null)
        {
            if (Settings.OptimisticLocking)
            {
                await ((IOptimisticLockingAggregationRepository)Repository).AddAsync(Context, key, original, answer, input.CancellationToken).ConfigureAwait(false);
            }
            else
            {
                await Repository.AddAsync(Context, key, answer, input.CancellationToken).ConfigureAwait(false);
            }
        }
        else
        {
            await CompleteAsync(complete, key, original, answer, aggregateFailed, completed).ConfigureAwait(false);
        }
    }

    /// <summary>Checks the completion conditions (Camel's <c>isCompleted</c>); returns what completed the group, or null.</summary>
    private string? IsCompleted(string key, Exchange exchange, Exchange newExchange)
    {
        if (Settings.CompletionFromBatchConsumer)
        {
            lock (_stateLock)
            {
                if (!_batchKeys.Contains(key))
                {
                    _batchKeys.Add(key);
                }

                _batchCounter++;
                var batchSize = newExchange.GetProperty(ExchangePropertyKeys.BatchSize, 0);
                if (newExchange.GetProperty(ExchangePropertyKeys.BatchComplete, false) || (batchSize > 0 && _batchCounter >= batchSize))
                {
                    _batchCounter = 0;
                    return AggregationCompletedBy.Consumer;
                }
            }
        }

        if (exchange.GetProperty(ExchangePropertyKeys.AggregationCompleteCurrentGroup, false))
        {
            exchange.RemoveProperty(ExchangePropertyKeys.AggregationCompleteCurrentGroup);
            return AggregationCompletedBy.Strategy;
        }

        if (_completionPredicate?.Matches(exchange) == true)
        {
            return AggregationCompletedBy.Predicate;
        }

        var aggregatedSize = exchange.GetProperty(ExchangePropertyKeys.AggregatedSize, 1);
        var sizeChecked = false;
        if (Settings.CompletionSizeExpression?.Evaluate(exchange) is { } value)
        {
            var completionSize = (int)Context.TypeConverter.Convert(typeof(int), exchange, value)!;
            if (completionSize > 0)
            {
                sizeChecked = true;
                if (aggregatedSize >= completionSize)
                {
                    return AggregationCompletedBy.Size;
                }
            }
        }

        if (!sizeChecked && Settings.CompletionSize > 0 && aggregatedSize >= Settings.CompletionSize)
        {
            return AggregationCompletedBy.Size;
        }

        return null;
    }

    /// <summary>(Re)starts the inactivity timeout of the group of <paramref name="key"/>.</summary>
    private void TrackTimeout(string key, Exchange exchange)
    {
        TimeSpan? timeout = null;
        if (Settings.CompletionTimeoutExpression?.Evaluate(exchange) is { } value)
        {
            var evaluated = value as TimeSpan? ??
                TimeSpan.FromMilliseconds((long)Context.TypeConverter.Convert(typeof(long), exchange, value)!);
            if (evaluated > TimeSpan.Zero)
            {
                timeout = evaluated;
            }
        }

        timeout ??= Settings.CompletionTimeout;
        if (timeout is { } t)
        {
            var deadline = Context.TimeProvider.GetUtcNow() + t;
            lock (_stateLock)
            {
                _timeouts[key] = new TimeoutEntry(deadline, t);
            }
        }
    }

    /// <summary>Completes the group (Camel's <c>doAggregationComplete</c>): all the batch's groups when completed by the consumer.</summary>
    private async Task CompleteAsync(string completedBy, string key, Exchange? original, Exchange answer, bool aggregateFailed, List<Exchange> completed)
    {
        if (completedBy != AggregationCompletedBy.Consumer)
        {
            answer.SetProperty(ExchangePropertyKeys.AggregatedCompletedBy, completedBy);
            if (await OnCompletionAsync(key, original, answer, fromTimeout: false, aggregateFailed, timeout: null).ConfigureAwait(false) is { } result)
            {
                completed.Add(result);
            }

            return;
        }

        List<string> keys;
        lock (_stateLock)
        {
            keys = [.. _batchKeys];
            _batchKeys.Clear();
        }

        if (!keys.Contains(key))
        {
            keys.Add(key);
        }

        foreach (var batchKey in keys)
        {
            if (batchKey == key)
            {
                answer.SetProperty(ExchangePropertyKeys.AggregatedCompletedBy, completedBy);
                if (await OnCompletionAsync(key, original, answer, fromTimeout: false, aggregateFailed, timeout: null).ConfigureAwait(false) is { } result)
                {
                    completed.Add(result);
                }
            }
            else
            {
                await CompleteGroupAsync(batchKey, completedBy, fromTimeout: false, timeout: null, completed).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Completes the stored group of <paramref name="key"/> (timeout, interval, force, other batch groups); returns whether there
    /// was one. A group changed concurrently (optimistic locking) is left alone.
    /// </summary>
    private async Task<bool> CompleteGroupAsync(string key, string completedBy, bool fromTimeout, TimeSpan? timeout, List<Exchange> completed)
    {
        var exchange = await Repository.GetAsync(Context, key).ConfigureAwait(false);
        if (exchange is null)
        {
            return false;
        }

        exchange.SetProperty(ExchangePropertyKeys.AggregatedCompletedBy, completedBy);
        if (timeout is { } t)
        {
            exchange.SetProperty(ExchangePropertyKeys.AggregatedTimeout, t);
        }

        try
        {
            if (await OnCompletionAsync(key, exchange, exchange, fromTimeout, aggregateFailed: false, timeout).ConfigureAwait(false) is { } result)
            {
                completed.Add(result);
            }

            return true;
        }
        catch (OptimisticLockingException)
        {
            _logger.LogTrace("Group {Key} changed concurrently while completing it by {CompletedBy}; left alone", key, completedBy);
            return false;
        }
    }

    private async Task<int> ForceCompletionOfAllGroupsCoreAsync(List<Exchange> completed, string? exceptKey)
    {
        var count = 0;
        foreach (var key in await Repository.GetKeysAsync().ConfigureAwait(false))
        {
            if (key != exceptKey &&
                await CompleteGroupAsync(key, AggregationCompletedBy.Force, fromTimeout: false, timeout: null, completed).ConfigureAwait(false))
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>
    /// Removes a completed group from the repository and the timeouts, closes its key and calls the strategy's timeout hook; returns
    /// the exchange to send, or null when it is discarded (Camel's <c>onCompletion</c>).
    /// </summary>
    private async Task<Exchange?> OnCompletionAsync(string key, Exchange? original, Exchange aggregated, bool fromTimeout, bool aggregateFailed, TimeSpan? timeout)
    {
        aggregated.SetProperty(ExchangePropertyKeys.AggregatedCorrelationKey, key);
        if (original is not null)
        {
            // The repository gets the completed exchange (a recoverable one keeps it until confirmed), identified as the version read.
            if (!ReferenceEquals(original, aggregated) &&
                original.Properties.TryGetValue(ExchangePropertyKeys.AggregationRepositoryVersion, out var version))
            {
                aggregated.SetProperty(ExchangePropertyKeys.AggregationRepositoryVersion, version);
            }

            // First, so an optimistic locking conflict leaves everything else as it was.
            await Repository.RemoveAsync(Context, key, aggregated).ConfigureAwait(false);
        }

        aggregated.RemoveProperty(ExchangePropertyKeys.AggregationRepositoryVersion);
        if (!fromTimeout)
        {
            lock (_stateLock)
            {
                _timeouts.Remove(key);
            }
        }

        if (Settings.CloseCorrelationKeyOnCompletion is { } capacity)
        {
            CloseKey(key, capacity);
        }

        if (fromTimeout)
        {
            try
            {
                AggregationStrategy.OnTimeout(aggregated, -1, -1, timeout ?? TimeSpan.Zero);
            }
            catch (Exception e)
            {
                _logger.LogWarning(e, "Aggregation strategy {Strategy} failed on timeout of group {Key}", AggregationStrategy, key);
            }
        }

        if ((fromTimeout && Settings.DiscardOnCompletionTimeout) || (aggregateFailed && Settings.DiscardOnAggregationFailure))
        {
            _logger.LogDebug("Discarding the aggregated exchange {ExchangeId} of correlation key {Key}", aggregated.ExchangeId, key);
            await Repository.ConfirmAsync(Context, aggregated.ExchangeId).ConfigureAwait(false);
            _redeliveries.TryRemove(aggregated.ExchangeId, out _);
            return null;
        }

        return aggregated;
    }

    private async Task<bool> DiscardGroupAsync(string key)
    {
        var exchange = await Repository.GetAsync(Context, key).ConfigureAwait(false);
        if (exchange is null)
        {
            return false;
        }

        try
        {
            await Repository.RemoveAsync(Context, key, exchange).ConfigureAwait(false);
        }
        catch (OptimisticLockingException)
        {
            return false;
        }

        lock (_stateLock)
        {
            _timeouts.Remove(key);
        }

        await Repository.ConfirmAsync(Context, exchange.ExchangeId).ConfigureAwait(false);
        return true;
    }

    /// <summary>Sends completed exchanges (Camel's <c>onSubmitCompletion</c>).</summary>
    private async Task SubmitAsync(List<Exchange> completed, Exchange? input)
    {
        foreach (var exchange in completed)
        {
            _inProgress[exchange.ExchangeId] = 0;
            try
            {
                AggregationStrategy.OnCompletion(exchange, input);
            }
            catch (Exception e)
            {
                exchange.Exception = e;
            }

            if (_slots is not null)
            {
                _sender.Start(exchange, _slots, OnSentAsync);
            }
            else
            {
                await _sender.SendAsync(exchange, OnSentAsync).ConfigureAwait(false);
            }
        }
    }

    private async ValueTask OnSentAsync(Exchange exchange)
    {
        try
        {
            if (!exchange.IsFailed && !exchange.IsRollbackOnly)
            {
                await Repository.ConfirmAsync(Context, exchange.ExchangeId).ConfigureAwait(false);
                _redeliveries.TryRemove(exchange.ExchangeId, out _);
            }
        }
        finally
        {
            _inProgress.TryRemove(exchange.ExchangeId, out _);
        }
    }

    private async Task ForceCompletionOnStopAsync()
    {
        try
        {
            await ForceCompletionOfAllGroupsAsync().ConfigureAwait(false);
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "Error completing the aggregation groups on stop");
        }
    }

    private async Task RestoreTimeoutsAsync(CancellationToken cancellationToken)
    {
        foreach (var key in await Repository.GetKeysAsync(cancellationToken).ConfigureAwait(false))
        {
            if (await Repository.GetAsync(Context, key, cancellationToken).ConfigureAwait(false) is { } exchange)
            {
                TrackTimeout(key, exchange);
            }
        }
    }

    /// <summary>Runs <paramref name="work"/> every <paramref name="period"/> on the context's clock until stopped.</summary>
    private async Task RunLoopAsync(TimeSpan period, bool fixedRate, Func<Task> work, CancellationToken token)
    {
        var time = Context.TimeProvider;
        var next = time.GetUtcNow() + period;
        while (!token.IsCancellationRequested)
        {
            try
            {
                var delay = fixedRate ? next - time.GetUtcNow() : period;
                await Task.Delay(delay > TimeSpan.Zero ? delay : TimeSpan.Zero, time, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            try
            {
                await work().ConfigureAwait(false);
            }
            catch (Exception e)
            {
                _logger.LogWarning(e, "Error in aggregator background task");
            }

            if (fixedRate)
            {
                var now = time.GetUtcNow();
                do
                {
                    next += period;
                }
                while (next <= now);
            }
        }
    }

    private async Task CheckTimeoutsAsync()
    {
        var now = Context.TimeProvider.GetUtcNow();
        List<string> expired;
        lock (_stateLock)
        {
            expired = [.. _timeouts.Where(t => t.Value.Deadline <= now).Select(t => t.Key)];
        }

        foreach (var key in expired)
        {
            var completed = new List<Exchange>();
            await LockAsync().ConfigureAwait(false);
            try
            {
                TimeoutEntry entry;
                lock (_stateLock)
                {
                    // An exchange may have restarted the timeout since the scan.
                    if (!_timeouts.TryGetValue(key, out entry) || entry.Deadline > now)
                    {
                        continue;
                    }

                    _timeouts.Remove(key);
                }

                await CompleteGroupAsync(key, AggregationCompletedBy.Timeout, fromTimeout: true, entry.Timeout, completed).ConfigureAwait(false);
            }
            finally
            {
                Unlock();
            }

            await SubmitAsync(completed, null).ConfigureAwait(false);
        }
    }

    private async Task CompleteByIntervalAsync()
    {
        var completed = new List<Exchange>();
        await LockAsync().ConfigureAwait(false);
        try
        {
            foreach (var key in await Repository.GetKeysAsync().ConfigureAwait(false))
            {
                await CompleteGroupAsync(key, AggregationCompletedBy.Interval, fromTimeout: false, timeout: null, completed).ConfigureAwait(false);
            }
        }
        finally
        {
            Unlock();
        }

        await SubmitAsync(completed, null).ConfigureAwait(false);
    }

    /// <summary>Resubmits unconfirmed completed exchanges of a recoverable repository (Camel's <c>RecoverTask</c>).</summary>
    private async Task RecoverAsync()
    {
        var recoverable = (IRecoverableAggregationRepository)Repository;
        foreach (var exchangeId in await recoverable.ScanAsync(Context).ConfigureAwait(false))
        {
            if (!IsRunAllowed)
            {
                return;
            }

            if (_inProgress.ContainsKey(exchangeId) ||
                await recoverable.RecoverAsync(Context, exchangeId).ConfigureAwait(false) is not { } exchange)
            {
                continue;
            }

            exchange.Message.SetHeader(CaravanHeaders.Redelivered, true);
            var attempts = _redeliveries.GetValueOrDefault(exchangeId);
            if (recoverable.MaximumRedeliveries > 0 && attempts >= recoverable.MaximumRedeliveries)
            {
                _logger.LogWarning(
                    "The recovered exchange {ExchangeId} is exhausted after {Attempts} attempts; moving it to the dead letter channel {Uri}",
                    exchangeId,
                    attempts,
                    recoverable.DeadLetterUri);
                exchange.Message.SetHeader(CaravanHeaders.RedeliveryCounter, attempts);
                exchange.Exception = null;
                exchange.IsRollbackOnly = false;
                try
                {
                    AggregationStrategy.OnCompletion(exchange, null);
                }
                catch (Exception e)
                {
                    _logger.LogWarning(e, "Aggregation strategy {Strategy} failed to complete the recovered exchange {ExchangeId}", AggregationStrategy, exchangeId);
                }

                var result = await _deadLetterTemplate!.SendAsync(recoverable.DeadLetterUri!, exchange).ConfigureAwait(false);
                if (result.Exception is { } failure)
                {
                    _logger.LogWarning(failure, "Failed to move the recovered exchange {ExchangeId} to {Uri}", exchangeId, recoverable.DeadLetterUri);
                }
                else
                {
                    await recoverable.ConfirmAsync(Context, exchangeId).ConfigureAwait(false);
                    _redeliveries.TryRemove(exchangeId, out _);
                }

                continue;
            }

            _redeliveries[exchangeId] = ++attempts;
            exchange.Message.SetHeader(CaravanHeaders.RedeliveryCounter, attempts);
            if (recoverable.MaximumRedeliveries > 0)
            {
                exchange.Message.SetHeader(CaravanHeaders.RedeliveryMaxCounter, recoverable.MaximumRedeliveries);
            }

            exchange.Exception = null;
            await SubmitAsync([exchange], null).ConfigureAwait(false);
        }
    }

    private readonly record struct TimeoutEntry(DateTimeOffset Deadline, TimeSpan Timeout);
}
