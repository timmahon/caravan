using System.Threading.Channels;

namespace Caravan.Components.Seda;

/// <summary>A named queue shared by the <c>seda</c> (or <c>vm</c>) endpoints using its name, and the consumers registered on it.</summary>
internal sealed class SedaQueue
{
    private readonly List<SedaConsumer> _consumers = [];
    private SemaphoreSlim? _multicastSlots;

    public SedaQueue(string key, int size)
    {
        Key = key;
        Size = size;
        Channel = size > 0
            ? System.Threading.Channels.Channel.CreateBounded<Exchange>(new BoundedChannelOptions(size) { FullMode = BoundedChannelFullMode.Wait })
            : System.Threading.Channels.Channel.CreateUnbounded<Exchange>();
    }

    /// <summary>The queue key, e.g. <c>seda://orders</c>.</summary>
    public string Key { get; }

    /// <summary>The capacity; zero or negative for unbounded.</summary>
    public int Size { get; }

    public Channel<Exchange> Channel { get; }

    public int Count => Channel.Reader.Count;

    public bool HasConsumers
    {
        get
        {
            lock (_consumers)
            {
                return _consumers.Count > 0;
            }
        }
    }

    public IReadOnlyList<SedaConsumer> Consumers
    {
        get
        {
            lock (_consumers)
            {
                return [.. _consumers];
            }
        }
    }

    public void AddConsumer(SedaConsumer consumer)
    {
        lock (_consumers)
        {
            _consumers.Add(consumer);
        }
    }

    public void RemoveConsumer(SedaConsumer consumer)
    {
        lock (_consumers)
        {
            _consumers.Remove(consumer);
        }
    }

    /// <summary>
    /// Limits how many exchanges are dispatched at once to multiple consumers (all consumers of the queue share it),
    /// so that each receives the messages in queue order when the count is 1. Created by the first multiple-consumers consumer.
    /// </summary>
    public SemaphoreSlim GetMulticastSlots(int count)
    {
        lock (_consumers)
        {
            return _multicastSlots ??= new SemaphoreSlim(count, count);
        }
    }

    /// <summary>Removes all queued exchanges; returns how many were removed.</summary>
    public int Purge()
    {
        var count = 0;
        while (Channel.Reader.TryRead(out _))
        {
            count++;
        }

        return count;
    }
}
