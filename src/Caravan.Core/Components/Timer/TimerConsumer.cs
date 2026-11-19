using Caravan.Spi;
using Caravan.Support;
using Microsoft.Extensions.Logging;

namespace Caravan.Components.Timer;

/// <summary>
/// Fires the timer's exchanges. The next fire is scheduled before the current one is processed (as
/// <c>java.util.Timer</c> does), and fires never overlap. While suspended, fires are skipped and not counted.
/// </summary>
internal sealed class TimerConsumer(TimerEndpoint endpoint, IProcessor processor) : ConsumerBase(endpoint, processor), IShutdownAware
{
    private readonly SemaphoreSlim _fireLock = new(1, 1);
    private CancellationTokenSource _stop = new();
    private CancellationTokenSource _force = new();
    private Task? _loop;
    private volatile bool _suspended;
    private long _counter;

    public int PendingExchangesSize => 0;

    public bool DeferShutdown(ShutdownRunningTask runningTask) => false;

    public void PrepareShutdown(ShutdownRunningTask runningTask, bool suspendOnly, bool forced)
    {
        if (forced)
        {
            _force.Cancel();
        }
    }

    protected override Task DoStartAsync(CancellationToken cancellationToken)
    {
        _stop = new CancellationTokenSource();
        _force = new CancellationTokenSource();
        _suspended = false;
        var token = _stop.Token;
        _loop = Task.Run(() => RunAsync(token), CancellationToken.None);
        return Task.CompletedTask;
    }

    protected override async Task DoStopAsync(CancellationToken cancellationToken)
    {
        await _stop.CancelAsync().ConfigureAwait(false);
        if (_loop is { } loop)
        {
            await loop.WaitAsync(_force.Token).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            _loop = null;
        }
    }

    protected override async Task DoSuspendAsync(CancellationToken cancellationToken)
    {
        _suspended = true;
        try
        {
            await _fireLock.WaitAsync(_force.Token).ConfigureAwait(false);
            _fireLock.Release();
        }
        catch (OperationCanceledException)
        {
            // forced
        }
    }

    protected override Task DoResumeAsync(CancellationToken cancellationToken)
    {
        _suspended = false;
        return Task.CompletedTask;
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        var time = Endpoint.Context.TimeProvider;
        try
        {
            if (endpoint.Delay > TimeSpan.Zero)
            {
                await Task.Delay(endpoint.Delay, time, cancellationToken).ConfigureAwait(false);
            }

            var scheduled = time.GetUtcNow();
            while (!cancellationToken.IsCancellationRequested)
            {
                var willFire = !_suspended;
                var last = willFire && endpoint.RepeatCount > 0 && Interlocked.Read(ref _counter) + 1 >= endpoint.RepeatCount;
                Task? next = null;
                if (endpoint.Period > TimeSpan.Zero && !last)
                {
                    scheduled += endpoint.Period;
                    var wait = endpoint.FixedRate ? scheduled - time.GetUtcNow() : endpoint.Period;
                    next = wait > TimeSpan.Zero ? Task.Delay(wait, time, cancellationToken) : Task.CompletedTask;
                }

                var fired = willFire && await FireAsync(cancellationToken).ConfigureAwait(false);
                if (last && !fired && endpoint.Period > TimeSpan.Zero)
                {
                    // Suspended while about to fire the last time: keep ticking.
                    next = Task.Delay(endpoint.Period, time, cancellationToken);
                }

                if (next is null)
                {
                    return;
                }

                await next.ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // stopped
        }
    }

    private async Task<bool> FireAsync(CancellationToken cancellationToken)
    {
        await _fireLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_suspended || !IsRunAllowed)
            {
                return false;
            }

            var counter = Interlocked.Increment(ref _counter);
            var exchange = CreateExchange();
            if (endpoint.IncludeMetadata)
            {
                var now = Endpoint.Context.TimeProvider.GetUtcNow();
                exchange.SetProperty(ExchangePropertyKeys.TimerCounter, counter);
                exchange.SetProperty(ExchangePropertyKeys.TimerName, endpoint.TimerName);
                exchange.SetProperty(ExchangePropertyKeys.TimerPeriod, (long)endpoint.Period.TotalMilliseconds);
                exchange.SetProperty(ExchangePropertyKeys.TimerFiredTime, now);
                exchange.Message.SetHeader(CaravanHeaders.FiredTime, now);
                exchange.Message.SetHeader(CaravanHeaders.MessageTimestamp, now.ToUnixTimeMilliseconds());
            }

            await Processor.ProcessAsync(exchange).ConfigureAwait(false);
            if (exchange.Exception is { } e)
            {
                Endpoint.Context.LoggerFactory.CreateLogger<TimerConsumer>()
                    .LogWarning(e, "Error processing exchange {ExchangeId} from {Uri}", exchange.ExchangeId, Endpoint.EndpointUri);
            }

            return true;
        }
        finally
        {
            _fireLock.Release();
        }
    }
}
