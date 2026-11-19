using Caravan.Processors;
using Caravan.Spi;

namespace Caravan.Model;

/// <summary>The <c>delay</c> EIP: delays the exchange on the context's clock; see <see cref="DelayProcessor"/>.</summary>
public sealed class DelayDefinition(IExpression delay) : ProcessorDefinition
{
    /// <summary>Computes the delay (a <see cref="TimeSpan"/> or milliseconds).</summary>
    public IExpression Delay { get; set; } = delay;

    /// <summary>Whether the delay is awaited without blocking the calling thread (default true).</summary>
    public bool AsyncDelayed { get; set; } = true;

    /// <summary>Accepted for parity with Camel; has no effect (there is no executor that could reject).</summary>
    public bool CallerRunsWhenRejected { get; set; } = true;

    /// <inheritdoc />
    public override string ShortName => "delay";

    /// <inheritdoc />
    public override string Label => $"delay[{Delay}]";

    /// <inheritdoc />
    public override IProcessor CreateProcessor(RouteContext routeContext) => new DelayProcessor(Delay, AsyncDelayed, CallerRunsWhenRejected);
}
