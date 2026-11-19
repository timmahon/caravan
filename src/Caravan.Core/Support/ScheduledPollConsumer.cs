using Caravan.Spi;
using Microsoft.Extensions.Logging;

namespace Caravan.Support;

/// <summary>
/// Base class for consumers that poll a resource on a schedule (Camel's <c>ScheduledPollConsumer</c>), such as
/// <c>scheduler</c> and <c>file</c>. Implement <see cref="PollAsync"/> to process what is available and return how many
/// messages were processed. Scheduling uses the context's <see cref="ICaravanContext.TimeProvider"/>.
/// <para>
/// Each run: skipped while suspended or backing off; then <see cref="IPollingConsumerPollStrategy.BeginAsync"/>,
/// <see cref="PollAsync"/> (when begin returned true), an empty message when idle (<see cref="SendEmptyMessageWhenIdle"/>)
/// and <see cref="IPollingConsumerPollStrategy.CommitAsync"/>; on failure <see cref="IPollingConsumerPollStrategy.RollbackAsync"/>
/// decides whether to retry at once. With <see cref="Greedy"/>, a run that processed messages is followed by another
/// one immediately.
/// </para>
/// </summary>
public abstract class ScheduledPollConsumer : ConsumerBase, IShutdownAware
{
    private readonly SemaphoreSlim _pollLock = new(1, 1);
    private CancellationTokenSource _stop = new();
    private CancellationTokenSource _force = new();
    private Task? _loop;
    private volatile bool _suspended;
    private long _counter;
    private int _idleCounter;
    private int _errorCounter;
    private int _backoffCounter;

    /// <summary>Creates the consumer.</summary>
    protected ScheduledPollConsumer(IEndpoint endpoint, IProcessor processor)
        : base(endpoint, processor)
    {
        Logger = endpoint.Context.LoggerFactory.CreateLogger(GetType());
    }

    /// <summary>Delay before the first poll (default 1s).</summary>
    public TimeSpan InitialDelay { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>Delay between polls (default 500ms).</summary>
    public TimeSpan Delay { get; set; } = TimeSpan.FromMilliseconds(500);

    /// <summary>True (default): <see cref="Delay"/> counts from the end of the previous poll; false: polls start at a fixed rate.</summary>
    public bool UseFixedDelay { get; set; } = true;

    /// <summary>Poll again immediately when the previous poll processed messages.</summary>
    public bool Greedy { get; set; }

    /// <summary>Send an empty message to the route when a poll found nothing.</summary>
    public bool SendEmptyMessageWhenIdle { get; set; }

    /// <summary>Number of scheduled polls to skip once a backoff threshold is reached (0 = no backoff).</summary>
    public int BackoffMultiplier { get; set; }

    /// <summary>Number of consecutive idle polls that trigger a backoff.</summary>
    public int BackoffIdleThreshold { get; set; }

    /// <summary>Number of consecutive failed polls that trigger a backoff.</summary>
    public int BackoffErrorThreshold { get; set; }

    /// <summary>Whether the scheduler starts with the consumer (default true); otherwise call <see cref="StartScheduling"/>.</summary>
    public bool StartScheduler { get; set; } = true;

    /// <summary>Maximum number of polls (0 = unlimited); the scheduler stops afterwards.</summary>
    public long RepeatCount { get; set; }

    /// <summary>Hooks around each poll.</summary>
    public IPollingConsumerPollStrategy PollStrategy { get; set; } = new DefaultPollingConsumerPollStrategy();

    /// <summary>The number of scheduled polls run since the consumer started (skipped and suspended runs excluded).</summary>
    public long Counter => Interlocked.Read(ref _counter);

    /// <summary>Whether the scheduler is running.</summary>
    public bool IsSchedulerStarted => _loop is { IsCompleted: false };

    /// <inheritdoc />
    public virtual int PendingExchangesSize => 0;

    /// <summary>The logger of the consumer.</summary>
    protected ILogger Logger { get; }

    /// <summary>Whether polling is allowed now (started and not suspended).</summary>
    protected bool IsPollAllowed => IsRunAllowed && !_suspended;

    /// <summary>Cancelled when a stop or suspension is forced (the graceful shutdown timed out).</summary>
    protected CancellationToken ForceToken => _force.Token;

    /// <summary>Starts the scheduler when <see cref="StartScheduler"/> is false (does nothing when it already runs).</summary>
    /// <exception cref="InvalidOperationException">The consumer is not started.</exception>
    public void StartScheduling()
    {
        if (!IsRunAllowed)
        {
            throw new InvalidOperationException($"{this} is not started");
        }

        if (IsSchedulerStarted)
        {
            return;
        }

        var token = _stop.Token;
        _loop = Task.Run(() => RunLoopAsync(token), CancellationToken.None);
    }

    /// <inheritdoc />
    public virtual bool DeferShutdown(ShutdownRunningTask runningTask) => false;

    /// <inheritdoc />
    public virtual void PrepareShutdown(ShutdownRunningTask runningTask, bool suspendOnly, bool forced)
    {
        if (forced)
        {
            _force.Cancel();
        }
    }

    /// <summary>Polls the resource and processes what is available; returns the number of messages processed.</summary>
    protected abstract Task<int> PollAsync(CancellationToken cancellationToken);

    /// <summary>Sends an empty exchange to the route (used with <see cref="SendEmptyMessageWhenIdle"/>).</summary>
    protected virtual async Task ProcessEmptyMessageAsync(CancellationToken cancellationToken)
    {
        var exchange = CreateExchange();
        await Processor.ProcessAsync(exchange).ConfigureAwait(false);
        LogFailure(exchange);
    }

    /// <summary>Logs a warning when the exchange failed (Camel's consumer exception handler).</summary>
    protected void LogFailure(Exchange exchange)
    {
        if (exchange.Exception is { } e)
        {
            Logger.LogWarning(e, "Error processing exchange {ExchangeId} from {Uri}", exchange.ExchangeId, Endpoint.EndpointUri);
        }
    }

    /// <summary>Validates the options and starts the scheduler (when <see cref="StartScheduler"/>).</summary>
    protected override Task DoStartAsync(CancellationToken cancellationToken)
    {
        if (BackoffMultiplier > 0 && BackoffIdleThreshold <= 0 && BackoffErrorThreshold <= 0)
        {
            throw new ArgumentException("backoffIdleThreshold and/or backoffErrorThreshold must be configured to a positive value when using backoffMultiplier");
        }

        _stop = new CancellationTokenSource();
        _force = new CancellationTokenSource();
        _suspended = false;
        Interlocked.Exchange(ref _counter, 0);
        _idleCounter = _errorCounter = _backoffCounter = 0;
        if (StartScheduler)
        {
            var token = _stop.Token;
            _loop = Task.Run(() => RunLoopAsync(token), CancellationToken.None);
        }

        return Task.CompletedTask;
    }

    /// <summary>Stops the scheduler and waits for the current poll (unless the shutdown is forced).</summary>
    protected override async Task DoStopAsync(CancellationToken cancellationToken)
    {
        await _stop.CancelAsync().ConfigureAwait(false);
        if (_loop is { } loop)
        {
            await loop.WaitAsync(_force.Token).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            _loop = null;
        }
    }

    /// <summary>Stops polling (the schedule keeps running) after the current poll completes.</summary>
    protected override async Task DoSuspendAsync(CancellationToken cancellationToken)
    {
        _suspended = true;
        try
        {
            await _pollLock.WaitAsync(_force.Token).ConfigureAwait(false);
            _pollLock.Release();
        }
        catch (OperationCanceledException)
        {
            // forced
        }
    }

    /// <summary>Allows polling again.</summary>
    protected override Task DoResumeAsync(CancellationToken cancellationToken)
    {
        _suspended = false;
        return Task.CompletedTask;
    }

    private async Task RunLoopAsync(CancellationToken cancellationToken)
    {
        var time = Endpoint.Context.TimeProvider;
        try
        {
            if (InitialDelay > TimeSpan.Zero)
            {
                await Task.Delay(InitialDelay, time, cancellationToken).ConfigureAwait(false);
            }

            var scheduled = time.GetUtcNow();
            while (!cancellationToken.IsCancellationRequested)
            {
                if (!await RunOnceAsync(cancellationToken).ConfigureAwait(false))
                {
                    return;
                }

                TimeSpan wait;
                if (UseFixedDelay)
                {
                    wait = Delay;
                }
                else
                {
                    scheduled += Delay;
                    wait = scheduled - time.GetUtcNow();
                }

                if (wait > TimeSpan.Zero)
                {
                    await Task.Delay(wait, time, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    await Task.Yield();
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // stopped
        }
    }

    /// <summary>One scheduled run; returns false when the scheduler should stop (repeat count reached).</summary>
    private async Task<bool> RunOnceAsync(CancellationToken cancellationToken)
    {
        if (!IsPollAllowed)
        {
            return true;
        }

        if (BackoffMultiplier > 0 && (BackoffIdleThreshold > 0 || BackoffErrorThreshold > 0))
        {
            var idle = BackoffIdleThreshold > 0 && _idleCounter >= BackoffIdleThreshold;
            var error = BackoffErrorThreshold > 0 && _errorCounter >= BackoffErrorThreshold;
            if (idle || error)
            {
                if (_backoffCounter++ < BackoffMultiplier)
                {
                    Logger.LogDebug("Skipping poll of {Uri} (backoff {Counter}/{Multiplier})", Endpoint.EndpointUri, _backoffCounter, BackoffMultiplier);
                    return true;
                }

                _idleCounter = _errorCounter = _backoffCounter = 0;
            }
        }

        await _pollLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!IsPollAllowed)
            {
                return true;
            }

            var count = Interlocked.Increment(ref _counter);
            await DoRunAsync(cancellationToken).ConfigureAwait(false);
            if (RepeatCount > 0 && count >= RepeatCount)
            {
                Logger.LogDebug("Stopping the scheduler of {Uri}: repeat count {RepeatCount} reached", Endpoint.EndpointUri, RepeatCount);
                return false;
            }

            return true;
        }
        finally
        {
            _pollLock.Release();
        }
    }

    private async Task DoRunAsync(CancellationToken cancellationToken)
    {
        var retryCounter = -1;
        var done = false;
        Exception? cause = null;
        var polled = 0;
        while (!done && !cancellationToken.IsCancellationRequested)
        {
            cause = null;
            done = true;
            if (!IsPollAllowed)
            {
                break;
            }

            try
            {
                retryCounter++;
                if (await PollStrategy.BeginAsync(this, Endpoint).ConfigureAwait(false))
                {
                    polled = await PollAsync(cancellationToken).ConfigureAwait(false);
                    if (polled == 0 && SendEmptyMessageWhenIdle)
                    {
                        await ProcessEmptyMessageAsync(cancellationToken).ConfigureAwait(false);
                    }
                }
                else
                {
                    Logger.LogDebug("Cannot begin polling {Uri} as the poll strategy returned false", Endpoint.EndpointUri);
                }

                // As in Camel, commit is called even when begin declined (with 0 messages).
                await PollStrategy.CommitAsync(this, Endpoint, polled).ConfigureAwait(false);
                if (polled > 0 && Greedy)
                {
                    done = false;
                    retryCounter = -1;
                    _idleCounter = _errorCounter = _backoffCounter = 0;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception e)
            {
                try
                {
                    if (await PollStrategy.RollbackAsync(this, Endpoint, retryCounter, e).ConfigureAwait(false))
                    {
                        done = false;
                    }
                    else
                    {
                        cause = e;
                    }
                }
                catch (Exception rollbackError)
                {
                    cause = rollbackError;
                }
            }
        }

        if (cause is not null)
        {
            _errorCounter++;
            _idleCounter = 0;
            Logger.LogWarning(cause, "Failed polling endpoint {Uri}. Will try again at next poll", Endpoint.EndpointUri);
        }
        else
        {
            _errorCounter = 0;
            _idleCounter = polled == 0 ? _idleCounter + 1 : 0;
        }
    }
}
