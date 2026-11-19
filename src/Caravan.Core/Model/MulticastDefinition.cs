using Caravan.Aggregation;
using Caravan.Processors;
using Caravan.Spi;

namespace Caravan.Model;

/// <summary>
/// The <c>multicast</c> EIP: each step of <see cref="Outputs"/> is a branch that processes its own copy of the exchange; the results
/// are aggregated (by default with <see cref="UseLatestAggregationStrategy"/>, as in Camel). See <see cref="MulticastProcessor"/>.
/// </summary>
public sealed class MulticastDefinition : FanOutDefinition
{
    /// <summary>The branches: each step is one branch (use <c>pipeline</c> to group several steps in a branch).</summary>
    public List<ProcessorDefinition> Outputs { get; } = [];

    /// <inheritdoc />
    public override IEnumerable<ProcessorDefinition> Children => Outputs;

    /// <inheritdoc />
    public override string ShortName => "multicast";

    /// <inheritdoc />
    public override IProcessor CreateProcessor(RouteContext routeContext)
    {
        var settings = CreateSettings(routeContext, UseLatestAggregationStrategy.Instance);
        var branches = new List<IProcessor>();
        foreach (var output in Outputs)
        {
            output.Parent = this;
            if (routeContext.CreateNodeProcessor(output) is { } branch)
            {
                branches.Add(branch);
            }
        }

        return new MulticastProcessor(branches, settings);
    }
}
