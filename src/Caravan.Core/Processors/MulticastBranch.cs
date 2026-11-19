using Caravan.Spi;

namespace Caravan.Processors;

/// <summary>A branch of a multicast-like EIP: the exchange to process and the processor that processes it.</summary>
/// <param name="processor">Processes the branch exchange.</param>
/// <param name="exchange">The branch exchange (a correlated copy of the input exchange).</param>
/// <param name="knownSize">The total number of branches, when known up front.</param>
internal sealed class MulticastBranch(IProcessor processor, Exchange exchange, int? knownSize)
{
    public IProcessor Processor { get; } = processor;

    public Exchange Exchange { get; } = exchange;

    public int? KnownSize { get; } = knownSize;
}
