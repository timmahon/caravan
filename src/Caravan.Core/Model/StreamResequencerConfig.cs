using Caravan.Processors;

namespace Caravan.Model;

/// <summary>Options of the stream resequencer (Camel's <c>StreamResequencerConfig</c>); see <see cref="StreamResequencerProcessor"/>.</summary>
public sealed class StreamResequencerConfig : ResequencerConfig
{
    /// <summary>The maximum number of exchanges held; senders wait while it is full (default 1000).</summary>
    public int Capacity { get; set; } = 1000;

    /// <summary>How long to wait for a missing sequence number before going on without it (default 1 s).</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>How often delivery is attempted when nothing signals new work (default 1 s).</summary>
    public TimeSpan DeliveryAttemptInterval { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>Fails exchanges older than the last delivered one with a <see cref="MessageRejectedException"/> instead of delivering them late.</summary>
    public bool RejectOld { get; set; }
}
