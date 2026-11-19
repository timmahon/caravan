using Caravan.Spi;

namespace Caravan.Model;

/// <summary>
/// The fallback block of a <c>circuitBreaker</c> (Camel's <c>onFallback</c>): runs on the exchange as it was before the breaker when the circuit
/// is open, the call failed, timed out or was rejected by the bulkhead.
/// </summary>
public sealed class OnFallbackDefinition : OutputDefinition
{
    /// <summary>
    /// Whether the fallback calls a remote service and is therefore protected by a circuit breaker of its own (Camel's
    /// <c>onFallbackViaNetwork</c>, from Hystrix): same configuration as the breaker, its circuit is named <c>&lt;id&gt;-fallback</c>.
    /// </summary>
    public bool FallbackViaNetwork { get; set; }

    /// <inheritdoc />
    public override string ShortName => FallbackViaNetwork ? "onFallbackViaNetwork" : "onFallback";

    /// <inheritdoc />
    public override IProcessor CreateProcessor(RouteContext routeContext) => routeContext.CreateBlockProcessor(Outputs, this);
}
