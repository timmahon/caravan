using Caravan.Spi;

namespace Caravan.Processors;

/// <summary>
/// The <c>sample</c> EIP (Camel's <c>SamplingThrottler</c>): lets one exchange through per <see cref="SamplePeriod"/> (the first one arriving
/// at least a period after the last one let through, measured on the context's clock), or every <see cref="MessageFrequency"/>th exchange; the
/// others are stopped (<see cref="Exchange.IsRouteStop"/>), as if filtered out.
/// </summary>
public sealed class SamplingProcessor : IProcessor
{
    private readonly Lock _lock = new();
    private DateTimeOffset? _lastSent;
    private long _count;

    /// <summary>Creates a processor letting one exchange through per <paramref name="samplePeriod"/>.</summary>
    /// <param name="samplePeriod">The period.</param>
    public SamplingProcessor(TimeSpan samplePeriod)
    {
        if (samplePeriod <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(samplePeriod), "The sample period must be positive");
        }

        SamplePeriod = samplePeriod;
    }

    /// <summary>Creates a processor letting every <paramref name="messageFrequency"/>th exchange through.</summary>
    /// <param name="messageFrequency">The frequency.</param>
    public SamplingProcessor(long messageFrequency)
    {
        if (messageFrequency <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(messageFrequency), "The message frequency must be positive");
        }

        MessageFrequency = messageFrequency;
    }

    /// <summary>The sample period; null when sampling by frequency.</summary>
    public TimeSpan? SamplePeriod { get; }

    /// <summary>Lets every Nth exchange through; null when sampling by period.</summary>
    public long? MessageFrequency { get; }

    /// <inheritdoc />
    public ValueTask ProcessAsync(Exchange exchange)
    {
        bool send;
        lock (_lock)
        {
            if (MessageFrequency is { } frequency)
            {
                send = ++_count % frequency == 0;
            }
            else
            {
                var now = exchange.Context.TimeProvider.GetUtcNow();
                send = _lastSent is not { } last || now >= last + SamplePeriod!.Value;
                if (send)
                {
                    _lastSent = now;
                }
            }
        }

        if (!send)
        {
            exchange.IsRouteStop = true;
        }

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public override string ToString() => MessageFrequency is { } f ? $"Sample[1 of {f}]" : $"Sample[1 per {SamplePeriod}]";
}
