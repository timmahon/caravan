using Caravan.Impl;
using Caravan.Spi;
using Caravan.Support;
using Microsoft.Extensions.Logging;

namespace Caravan.Components.Seda;

/// <summary>
/// Takes exchanges from the queue with <see cref="SedaEndpoint.ConcurrentConsumers"/> background tasks and routes them.
/// Suspending stops taking (the queue keeps its messages) once the current exchanges complete; stopping waits for them.
/// During a graceful shutdown the consumer is deferred: it stops taking at once with
/// <see cref="ShutdownRunningTask.CompleteCurrentTaskOnly"/>, or drains the queue with <see cref="ShutdownRunningTask.CompleteAllTasks"/>.
/// When the shutdown is forced, the exchanges being processed are cancelled and the consumer stops waiting for them.
/// </summary>
internal sealed class SedaConsumer : ConsumerBase, IShutdownAware
{
    private readonly SedaEndpoint _endpoint;
    private readonly ILogger _logger;
    private readonly object _gate = new();
    private CancellationTokenSource _stop = new();
    private CancellationTokenSource _force = new();
    private CancellationTokenSource? _exchanges;
    private TaskCompletionSource _resumed = NewSignal();
    private TaskCompletionSource? _idle;
    private Task[] _workers = [];
    private bool _paused;
    private int _active;

    public SedaConsumer(SedaEndpoint endpoint, IProcessor processor)
        : base(endpoint, processor)
    {
        _endpoint = endpoint;
        _logger = endpoint.Context.LoggerFactory.CreateLogger<SedaConsumer>();
    }

    /// <summary>Whether the consumer currently takes exchanges (used to pick the receivers of multiple-consumer copies).</summary>
    internal bool IsTaking
    {
        get
        {
            lock (_gate)
            {
                return Status == ServiceStatus.Started && !_paused;
            }
        }
    }

    public int PendingExchangesSize
    {
        get
        {
            if (_endpoint.PurgeWhenStopping)
            {
                _endpoint.Queue.Purge();
            }

            return _endpoint.Queue.Count;
        }
    }

    public bool DeferShutdown(ShutdownRunningTask runningTask) => true;

    public void PrepareShutdown(ShutdownRunningTask runningTask, bool suspendOnly, bool forced)
    {
        if (forced)
        {
            _force.Cancel();
            _exchanges?.Cancel();
            return;
        }

        if (suspendOnly || runningTask == ShutdownRunningTask.CompleteCurrentTaskOnly)
        {
            Pause();
        }
    }

    protected override Task DoStartAsync(CancellationToken cancellationToken)
    {
        _stop = new CancellationTokenSource();
        _force = new CancellationTokenSource();
        _exchanges = CancellationTokenSource.CreateLinkedTokenSource(Endpoint.Context.ShutdownToken);
        lock (_gate)
        {
            _paused = false;
            _active = 0;
            _resumed.TrySetResult();
        }

        _endpoint.Queue.AddConsumer(this);
        var token = _stop.Token;
        _workers = Enumerable.Range(0, _endpoint.ConcurrentConsumers)
            .Select(_ => Task.Run(() => RunAsync(token), CancellationToken.None))
            .ToArray();
        return Task.CompletedTask;
    }

    protected override async Task DoStopAsync(CancellationToken cancellationToken)
    {
        _endpoint.Queue.RemoveConsumer(this);
        await _stop.CancelAsync().ConfigureAwait(false);
        var workers = Task.WhenAll(_workers);
        await workers.WaitAsync(_force.Token).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        _workers = [];
        if (_endpoint.PurgeWhenStopping)
        {
            _endpoint.Queue.Purge();
        }

        if (workers.IsCompleted)
        {
            // When forced, abandoned workers may still link to it; leave it to the GC then.
            _exchanges?.Dispose();
        }

        _exchanges = null;
    }

    protected override async Task DoSuspendAsync(CancellationToken cancellationToken)
    {
        Pause();
        Task idle;
        lock (_gate)
        {
            if (_active == 0)
            {
                return;
            }

            _idle ??= NewSignal();
            idle = _idle.Task;
        }

        await idle.WaitAsync(_force.Token).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
    }

    protected override Task DoResumeAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _paused = false;
            _resumed.TrySetResult();
        }

        return Task.CompletedTask;
    }

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private void Pause()
    {
        lock (_gate)
        {
            if (!_paused)
            {
                _paused = true;
                _resumed = NewSignal();
            }
        }
    }

    private async Task RunAsync(CancellationToken stop)
    {
        var reader = _endpoint.Queue.Channel.Reader;
        var slots = _endpoint.MultipleConsumers ? _endpoint.Queue.GetMulticastSlots(_endpoint.ConcurrentConsumers) : null;
        while (!stop.IsCancellationRequested)
        {
            Task? resumed;
            lock (_gate)
            {
                resumed = _paused ? _resumed.Task : null;
            }

            var holdsSlot = false;
            try
            {
                if (resumed is not null)
                {
                    await resumed.WaitAsync(stop).ConfigureAwait(false);
                    continue;
                }

                if (slots is not null)
                {
                    await slots.WaitAsync(stop).ConfigureAwait(false);
                    holdsSlot = true;
                }

                if (!await reader.WaitToReadAsync(stop).ConfigureAwait(false))
                {
                    return;
                }

                Exchange? item;
                lock (_gate)
                {
                    if (_paused || stop.IsCancellationRequested || !reader.TryRead(out item))
                    {
                        continue;
                    }

                    _active++;
                }

                await ProcessItemAsync(item).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested)
            {
                return;
            }
            finally
            {
                if (holdsSlot)
                {
                    slots!.Release();
                }
            }
        }
    }

    private async Task ProcessItemAsync(Exchange item)
    {
        try
        {
            await DispatchAsync(item).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "Error processing exchange {ExchangeId} from {Uri}", item.ExchangeId, Endpoint.EndpointUri);
        }
        finally
        {
            lock (_gate)
            {
                if (--_active == 0 && _idle is { } idle)
                {
                    _idle = null;
                    idle.TrySetResult();
                }
            }
        }
    }

    private async Task DispatchAsync(Exchange item)
    {
        if (item.CancellationToken.IsCancellationRequested)
        {
            // The waiting producer gave up (timeout): Camel removes the exchange from the queue.
            _logger.LogDebug("Skipping exchange {ExchangeId} from {Uri}: cancelled by its producer", item.ExchangeId, Endpoint.EndpointUri);
            return;
        }

        var exchange = item.CopyToContext(Endpoint.Context);
        var consumerToken = _exchanges?.Token ?? Endpoint.Context.ShutdownToken;
        CancellationTokenSource? linked = null;
        if (exchange.CancellationToken.CanBeCanceled)
        {
            linked = CancellationTokenSource.CreateLinkedTokenSource(exchange.CancellationToken, consumerToken);
            exchange.CancellationToken = linked.Token;
        }
        else
        {
            exchange.CancellationToken = consumerToken;
        }

        try
        {
            var receivers = _endpoint.MultipleConsumers
                ? _endpoint.Queue.Consumers.Where(c => ReferenceEquals(c, this) || c.IsTaking).ToList()
                : null;
            if (receivers is null || receivers.Count <= 1)
            {
                await Processor.ProcessAsync(exchange).ConfigureAwait(false);
            }
            else
            {
                await MulticastAsync(exchange, receivers).ConfigureAwait(false);
            }

            if (exchange.Exception is { } e)
            {
                _logger.LogWarning(e, "Error processing exchange {ExchangeId} from {Uri}", exchange.ExchangeId, Endpoint.EndpointUri);
            }
        }
        finally
        {
            linked?.Dispose();
        }
    }

    /// <summary>Sends a copy to every receiver, then completes the original with the last copy's result.</summary>
    private async Task MulticastAsync(Exchange exchange, List<SedaConsumer> receivers)
    {
        var unitOfWork = new DefaultUnitOfWork(exchange);
        exchange.UnitOfWork = unitOfWork;
        var copies = receivers.Select(r => (Receiver: r, Copy: CopyFor(exchange, r))).ToList();
        if (_endpoint.ConcurrentConsumers > 1)
        {
            await Task.WhenAll(copies.Select(c => c.Receiver.Processor.ProcessAsync(c.Copy).AsTask())).ConfigureAwait(false);
        }
        else
        {
            foreach (var (receiver, copy) in copies)
            {
                await receiver.Processor.ProcessAsync(copy).ConfigureAwait(false);
            }
        }

        var failed = copies.Select(c => c.Copy).FirstOrDefault(c => c.IsFailed);
        exchange.CopyResultsFrom(failed ?? copies[^1].Copy);
        await unitOfWork.DoneAsync(exchange).ConfigureAwait(false);
        exchange.UnitOfWork = null;
    }

    private static Exchange CopyFor(Exchange exchange, SedaConsumer receiver)
    {
        var copy = ReferenceEquals(receiver.Endpoint.Context, exchange.Context) ? exchange.Copy() : exchange.CopyToContext(receiver.Endpoint.Context);
        copy.UnitOfWork = null;
        return copy;
    }
}
