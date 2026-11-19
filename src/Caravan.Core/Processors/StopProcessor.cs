using Caravan.Spi;

namespace Caravan.Processors;

/// <summary>The <c>stop</c> EIP: marks the exchange so that no further routing happens, including in calling routes.</summary>
public sealed class StopProcessor : IProcessor
{
    /// <inheritdoc />
    public ValueTask ProcessAsync(Exchange exchange)
    {
        exchange.IsRouteStop = true;
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public override string ToString() => "Stop";
}
