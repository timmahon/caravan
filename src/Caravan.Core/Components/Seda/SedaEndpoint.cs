using Caravan.Spi;
using Caravan.Support;

namespace Caravan.Components.Seda;

/// <summary>
/// A <c>seda</c> (or <c>vm</c>) endpoint. The producer puts a copy of the exchange on the queue; consumers take
/// exchanges from it on background tasks and route them.
/// </summary>
public sealed class SedaEndpoint : EndpointBase
{
    internal SedaEndpoint(string uri, SedaComponent component, string name, SedaQueue queue)
        : base(uri, component, component.Context!)
    {
        Name = name;
        Queue = queue;
    }

    /// <summary>The queue name (the URI path).</summary>
    public string Name { get; }

    /// <summary>The queue capacity (<c>size</c> option; default 1000, zero or negative for unbounded). Fixed by the first endpoint of the queue.</summary>
    public int Size => Queue.Size;

    /// <summary>The number of exchanges waiting in the queue.</summary>
    public int CurrentQueueSize => Queue.Count;

    /// <summary>The number of consumers on the queue (all endpoints, and all contexts for <c>vm</c>).</summary>
    public int ConsumerCount => Queue.Consumers.Count;

    /// <summary>Number of tasks taking exchanges from the queue per consumer (default 1).</summary>
    public int ConcurrentConsumers { get; set; } = 1;

    /// <summary>
    /// Publish-subscribe: every consumer receives a copy of each exchange (default false: consumers compete).
    /// With <see cref="ConcurrentConsumers"/> 1, one message is dispatched at a time (each consumer sees queue order) and the
    /// copies are processed one after the other; above 1, that many messages are dispatched at once and copies run in parallel.
    /// </summary>
    public bool MultipleConsumers { get; set; }

    /// <summary>Whether the producer waits for the consumer to complete the exchange (default <see cref="Caravan.WaitForTaskToComplete.IfReplyExpected"/>).</summary>
    public WaitForTaskToComplete WaitForTaskToComplete { get; set; } = WaitForTaskToComplete.IfReplyExpected;

    /// <summary>How long a waiting producer waits before failing with <see cref="ExchangeTimedOutException"/> (default 30s; zero = forever).</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>When the queue is full, wait for room instead of failing (default false).</summary>
    public bool BlockWhenFull { get; set; }

    /// <summary>With <see cref="BlockWhenFull"/>, how long to wait for room before failing with <see cref="ExchangeTimedOutException"/> (zero = forever).</summary>
    public TimeSpan OfferTimeout { get; set; }

    /// <summary>When the queue is full, drop the message silently instead of failing (default false).</summary>
    public bool DiscardWhenFull { get; set; }

    /// <summary>Drop messages sent while the queue has no consumer (default false).</summary>
    public bool DiscardIfNoConsumers { get; set; }

    /// <summary>Fail with <see cref="SedaConsumerNotAvailableException"/> when sending while the queue has no consumer (default false).</summary>
    public bool FailIfNoConsumers { get; set; }

    /// <summary>Remove the queued messages when a consumer stops (default false).</summary>
    public bool PurgeWhenStopping { get; set; }

    /// <summary>
    /// Kept for URI compatibility with Camel and ignored: consumers wait on the channel asynchronously and react to
    /// stop and suspend immediately, so there is no poll interval.
    /// </summary>
    public TimeSpan PollTimeout { get; set; } = TimeSpan.FromSeconds(1);

    internal SedaQueue Queue { get; }

    /// <summary>Whether unknown URI options are ignored (the <c>stub</c> component).</summary>
    internal bool Lenient { get; init; }

    /// <inheritdoc />
    public override bool IsLenientProperties => Lenient;

    /// <summary>Removes all messages waiting in the queue.</summary>
    public void PurgeQueue() => Queue.Purge();

    /// <inheritdoc />
    public override IProducer CreateProducer() => new SedaProducer(this);

    /// <inheritdoc />
    public override IConsumer CreateConsumer(IProcessor processor)
    {
        if (ConcurrentConsumers < 1)
        {
            throw new ArgumentException($"The option concurrentConsumers must be at least 1, was {ConcurrentConsumers}");
        }

        return new SedaConsumer(this, processor);
    }

    /// <summary>Creates a polling consumer that takes exchanges straight from the queue.</summary>
    public override IPollingConsumer CreatePollingConsumer() => new SedaPollingConsumer(this);
}
