using Caravan.Processors;

namespace Caravan.Model;

/// <summary>Options of the batch resequencer (Camel's <c>BatchResequencerConfig</c>); see <see cref="BatchResequencerProcessor"/>.</summary>
public sealed class BatchResequencerConfig : ResequencerConfig
{
    /// <summary>A batch is emitted as soon as it holds this many exchanges (default 100).</summary>
    public int BatchSize { get; set; } = 100;

    /// <summary>A batch is emitted at the latest this long after its first exchange arrived (default 1 s).</summary>
    public TimeSpan BatchTimeout { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>Keeps exchanges whose sort keys are equal; by default only the first one of a batch is kept.</summary>
    public bool AllowDuplicates { get; set; }

    /// <summary>Emits each batch in descending order.</summary>
    public bool Reverse { get; set; }
}
