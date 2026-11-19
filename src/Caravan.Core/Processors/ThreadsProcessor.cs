using Caravan.Model;
using Caravan.Spi;
using Caravan.Support;

namespace Caravan.Processors;

/// <summary>
/// Runs a block on a bounded pool of workers (the <c>threads</c> EIP), with Java's <c>ThreadPoolExecutor</c> rules: up to
/// <see cref="PoolSize"/> exchanges run at once; further ones wait in a queue of <see cref="MaxQueueSize"/>; when the queue is full extra workers
/// start up to <see cref="MaxPoolSize"/>; beyond that the exchange is rejected (<see cref="ThreadPoolRejectedPolicy"/>). Workers are tasks on the
/// thread pool; the caller awaits the exchange's completion without blocking a thread.
/// </summary>
public sealed class ThreadsProcessor : ServiceSupport, INavigableProcessor
{
    private readonly Lock _gate = new();
    private readonly Queue<WorkItem> _queue = new();
    private readonly IProcessor _block;
    private int _active;

    /// <summary>Creates the processor.</summary>
    /// <param name="block">The steps run by the workers.</param>
    /// <param name="poolSize">The number of workers kept.</param>
    /// <param name="maxPoolSize">The maximum number of workers.</param>
    /// <param name="maxQueueSize">The queue capacity (0: none, negative: unbounded).</param>
    /// <param name="rejectedPolicy">What happens when the pool and queue are full.</param>
    /// <param name="name">A name for the pool.</param>
    public ThreadsProcessor(IProcessor block, int poolSize, int maxPoolSize, int maxQueueSize, ThreadPoolRejectedPolicy rejectedPolicy, string? name = null)
    {
        _block = block;
        PoolSize = poolSize;
        MaxPoolSize = maxPoolSize;
        MaxQueueSize = maxQueueSize;
        RejectedPolicy = rejectedPolicy;
        Name = name;
    }

    /// <summary>The number of workers kept.</summary>
    public int PoolSize { get; }

    /// <summary>The maximum number of workers.</summary>
    public int MaxPoolSize { get; }

    /// <summary>The queue capacity (0: none, negative: unbounded).</summary>
    public int MaxQueueSize { get; }

    /// <summary>What happens when the pool and queue are full.</summary>
    public ThreadPoolRejectedPolicy RejectedPolicy { get; }

    /// <summary>The name of the pool.</summary>
    public string? Name { get; }

    /// <summary>The number of exchanges being processed by workers.</summary>
    public int ActiveCount
    {
        get
        {
            lock (_gate)
            {
                return _active;
            }
        }
    }

    /// <summary>The number of exchanges waiting for a worker.</summary>
    public int QueueSize
    {
        get
        {
            lock (_gate)
            {
                return _queue.Count;
            }
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<IProcessor> Next => [_block];

    /// <inheritdoc />
    public async ValueTask ProcessAsync(Exchange exchange)
    {
        var item = new WorkItem(exchange);
        bool run;
        lock (_gate)
        {
            if (!IsRunAllowed)
            {
                throw new RejectedExecutionException($"threads{(Name is null ? string.Empty : " " + Name)} is not started", exchange);
            }

            if (_active < PoolSize)
            {
                _active++;
                run = true;
            }
            else if (MaxQueueSize < 0 || _queue.Count < MaxQueueSize)
            {
                _queue.Enqueue(item);
                run = false;
            }
            else if (_active < MaxPoolSize)
            {
                _active++;
                run = true;
            }
            else if (RejectedPolicy == ThreadPoolRejectedPolicy.CallerRuns)
            {
                item = null;
                run = false;
            }
            else
            {
                throw new RejectedExecutionException(
                    $"threads{(Name is null ? string.Empty : " " + Name)} rejected the exchange: {_active} workers busy and {_queue.Count} exchanges queued",
                    exchange);
            }
        }

        if (item is null)
        {
            await _block.ProcessAsync(exchange).ConfigureAwait(false);
            return;
        }

        if (run)
        {
            _ = Task.Run(() => WorkAsync(item));
        }

        await item.Done.Task.ConfigureAwait(false);
    }

    /// <summary>Fails the exchanges still queued.</summary>
    protected override Task DoStopAsync(CancellationToken cancellationToken)
    {
        List<WorkItem> pending;
        lock (_gate)
        {
            pending = [.. _queue];
            _queue.Clear();
        }

        foreach (var item in pending)
        {
            item.Exchange.Exception = new RejectedExecutionException("threads stopped before processing the exchange", item.Exchange);
            item.Done.TrySetResult();
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public override string ToString() => $"Threads[{Name ?? "pool"}, {PoolSize}..{MaxPoolSize}]";

    private async Task WorkAsync(WorkItem item)
    {
        while (true)
        {
            try
            {
                await _block.ProcessAsync(item.Exchange).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                item.Exchange.Exception = e;
            }

            item.Done.TrySetResult();
            lock (_gate)
            {
                if (!_queue.TryDequeue(out var next))
                {
                    _active--;
                    return;
                }

                item = next;
            }
        }
    }

    private sealed class WorkItem(Exchange exchange)
    {
        public Exchange Exchange { get; } = exchange;

        public TaskCompletionSource Done { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
