using System.Collections.Concurrent;
using Caravan.Impl;
using Caravan.Spi;
using Microsoft.Extensions.Logging;

namespace Caravan.Processors;

/// <summary>
/// Sends exchanges that an EIP creates on its own (the aggregator's completed groups, the resequencer's reordered exchanges) through
/// a nested block, detached from the exchange that triggered them: each gets its own unit of work, is tracked in the inflight
/// repository under the route id, and failures that remain after the block's error handling are logged.
/// </summary>
internal sealed class DetachedExchangeSender(IProcessor processor, string routeId, string description)
{
    private readonly ConcurrentDictionary<long, Task> _background = new();
    private long _sequence;
    private int _pending;

    /// <summary>The number of exchanges being sent (inline or in the background), including those waiting for a slot.</summary>
    public int Pending => Volatile.Read(ref _pending);

    /// <summary>Sends <paramref name="exchange"/> and waits for it.</summary>
    public async Task SendAsync(Exchange exchange, Func<Exchange, ValueTask>? onDone = null)
    {
        Interlocked.Increment(ref _pending);
        try
        {
            await RunAsync(exchange, onDone).ConfigureAwait(false);
        }
        finally
        {
            Interlocked.Decrement(ref _pending);
        }
    }

    /// <summary>Sends <paramref name="exchange"/> on the thread pool, at most as many at a time as <paramref name="slots"/> allows.</summary>
    public void Start(Exchange exchange, SemaphoreSlim? slots, Func<Exchange, ValueTask>? onDone = null)
    {
        Interlocked.Increment(ref _pending);
        var id = Interlocked.Increment(ref _sequence);
        var task = Task.Run(async () =>
        {
            try
            {
                if (slots is null)
                {
                    await RunAsync(exchange, onDone).ConfigureAwait(false);
                    return;
                }

                await slots.WaitAsync().ConfigureAwait(false);
                try
                {
                    await RunAsync(exchange, onDone).ConfigureAwait(false);
                }
                finally
                {
                    slots.Release();
                }
            }
            finally
            {
                Interlocked.Decrement(ref _pending);
                _background.TryRemove(id, out _);
            }
        });
        _background.TryAdd(id, task);
        if (task.IsCompleted)
        {
            _background.TryRemove(id, out _);
        }
    }

    /// <summary>Waits for the exchanges sent in the background.</summary>
    public async Task DrainAsync(CancellationToken cancellationToken)
    {
        while (!_background.IsEmpty)
        {
            await Task.WhenAll(_background.Values).WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task RunAsync(Exchange exchange, Func<Exchange, ValueTask>? onDone)
    {
        var context = exchange.Context;
        exchange.CancellationToken = context.ShutdownToken;
        exchange.CurrentRouteId = routeId;
        exchange.FromRouteId ??= routeId;
        var unitOfWork = new DefaultUnitOfWork(exchange);
        exchange.UnitOfWork = unitOfWork;
        context.InflightRepository.Add(exchange, routeId);
        try
        {
            try
            {
                if (exchange.Exception is null)
                {
                    await processor.ProcessAsync(exchange).ConfigureAwait(false);
                }
            }
            catch (Exception e)
            {
                exchange.Exception = e;
            }

            await unitOfWork.DoneAsync(exchange).ConfigureAwait(false);
            exchange.UnitOfWork = null;
            if (exchange.Exception is { } failure)
            {
                context.LoggerFactory.CreateLogger<DetachedExchangeSender>().LogWarning(
                    failure,
                    "Error processing {Description} exchange {ExchangeId} in route {RouteId}",
                    description,
                    exchange.ExchangeId,
                    routeId);
            }

            if (onDone is not null)
            {
                await onDone(exchange).ConfigureAwait(false);
            }
        }
        catch (Exception e)
        {
            context.LoggerFactory.CreateLogger<DetachedExchangeSender>().LogWarning(
                e,
                "Error completing {Description} exchange {ExchangeId} in route {RouteId}",
                description,
                exchange.ExchangeId,
                routeId);
        }
        finally
        {
            exchange.UnitOfWork = null;
            context.InflightRepository.Remove(exchange, routeId);
        }
    }
}
