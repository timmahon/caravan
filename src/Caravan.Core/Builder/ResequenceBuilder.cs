using Caravan.Model;

namespace Caravan.Builder;

/// <summary>
/// Chooses the algorithm of a <c>resequence</c> EIP: <c>r =&gt; r.Batch(batchSize: 10)</c> or <c>r =&gt; r.Stream(capacity: 100)</c>.
/// </summary>
public sealed class ResequenceBuilder
{
    /// <summary>Creates a builder for <paramref name="definition"/>.</summary>
    /// <param name="definition">The definition to configure.</param>
    public ResequenceBuilder(ResequenceDefinition definition)
    {
        Definition = definition;
    }

    /// <summary>The definition being configured.</summary>
    public ResequenceDefinition Definition { get; }

    /// <summary>Collects batches, sorts them by the expression and emits them (the default); see <see cref="BatchResequencerConfig"/>.</summary>
    /// <param name="batchSize">A batch is emitted as soon as it holds this many exchanges (default 100).</param>
    /// <param name="batchTimeout">A batch is emitted at the latest this long after its first exchange (default 1 s).</param>
    /// <param name="allowDuplicates">Keep exchanges with equal keys (by default only the first is kept).</param>
    /// <param name="reverse">Emit in descending order.</param>
    /// <param name="ignoreInvalidExchanges">Drop exchanges whose expression fails or yields null instead of failing them.</param>
    public ResequenceBuilder Batch(int batchSize = 100, TimeSpan? batchTimeout = null, bool allowDuplicates = false, bool reverse = false, bool ignoreInvalidExchanges = false)
    {
        Definition.Config = new BatchResequencerConfig
        {
            BatchSize = batchSize,
            BatchTimeout = batchTimeout ?? TimeSpan.FromSeconds(1),
            AllowDuplicates = allowDuplicates,
            Reverse = reverse,
            IgnoreInvalidExchanges = ignoreInvalidExchanges,
        };
        return this;
    }

    /// <summary>Emits exchanges as soon as their sequence numbers (consecutive longs) are in order; see <see cref="StreamResequencerConfig"/>.</summary>
    /// <param name="capacity">The maximum number of exchanges held (default 1000).</param>
    /// <param name="timeout">How long to wait for a missing sequence number (default 1 s).</param>
    /// <param name="deliveryAttemptInterval">How often delivery is attempted when nothing signals new work (default 1 s).</param>
    /// <param name="ignoreInvalidExchanges">Drop exchanges without a valid sequence number instead of failing them.</param>
    /// <param name="rejectOld">Fail exchanges older than the last delivered one with a <see cref="MessageRejectedException"/>.</param>
    public ResequenceBuilder Stream(int capacity = 1000, TimeSpan? timeout = null, TimeSpan? deliveryAttemptInterval = null, bool ignoreInvalidExchanges = false, bool rejectOld = false)
    {
        Definition.Config = new StreamResequencerConfig
        {
            Capacity = capacity,
            Timeout = timeout ?? TimeSpan.FromSeconds(1),
            DeliveryAttemptInterval = deliveryAttemptInterval ?? TimeSpan.FromSeconds(1),
            IgnoreInvalidExchanges = ignoreInvalidExchanges,
            RejectOld = rejectOld,
        };
        return this;
    }
}
