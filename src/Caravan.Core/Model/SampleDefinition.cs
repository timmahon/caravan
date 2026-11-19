using Caravan.Processors;
using Caravan.Spi;

namespace Caravan.Model;

/// <summary>
/// The <c>sample</c> EIP: lets one exchange through per <see cref="SamplePeriod"/> (default 1 second) or every <see cref="MessageFrequency"/>th
/// exchange, stopping the others; see <see cref="SamplingProcessor"/>.
/// </summary>
public sealed class SampleDefinition : ProcessorDefinition
{
    /// <summary>The sample period (default 1 second); ignored when <see cref="MessageFrequency"/> is set.</summary>
    public TimeSpan SamplePeriod { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>When set, lets every Nth exchange through instead of one per period.</summary>
    public long? MessageFrequency { get; set; }

    /// <inheritdoc />
    public override string ShortName => "sample";

    /// <inheritdoc />
    public override string Label => MessageFrequency is { } f ? $"sample[1 of {f}]" : $"sample[{SamplePeriod}]";

    /// <inheritdoc />
    public override IProcessor CreateProcessor(RouteContext routeContext) =>
        MessageFrequency is { } frequency ? new SamplingProcessor(frequency) : new SamplingProcessor(SamplePeriod);
}
