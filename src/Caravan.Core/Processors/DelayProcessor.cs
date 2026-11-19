using Caravan.Spi;
using Caravan.Support;

namespace Caravan.Processors;

/// <summary>
/// The <c>delay</c> EIP (Camel's <c>Delayer</c>): waits for the delay computed by <see cref="Delay"/> (a <see cref="TimeSpan"/> or a number of
/// milliseconds; null, zero or negative is no delay) on the context's <see cref="ICaravanContext.TimeProvider"/>, then lets the exchange
/// continue. A cancelled <see cref="Exchange.CancellationToken"/> ends the wait and fails the exchange.
/// </summary>
/// <remarks>
/// With <see cref="AsyncDelayed"/> (the default) the wait does not hold a thread; without it the calling thread is blocked, as Camel's
/// synchronous delayer. <see cref="CallerRunsWhenRejected"/> is accepted for parity but has no effect: there is no executor that could reject.
/// </remarks>
public sealed class DelayProcessor(IExpression delay, bool asyncDelayed = true, bool callerRunsWhenRejected = true) : IProcessor
{
    /// <summary>Computes the delay.</summary>
    public IExpression Delay { get; } = delay;

    /// <summary>Whether the delay is awaited without blocking the calling thread.</summary>
    public bool AsyncDelayed { get; } = asyncDelayed;

    /// <summary>Accepted for parity with Camel; has no effect.</summary>
    public bool CallerRunsWhenRejected { get; } = callerRunsWhenRejected;

    /// <inheritdoc />
    public ValueTask ProcessAsync(Exchange exchange)
    {
        var delay = ExchangeHelper.ToDelay(exchange, Delay.Evaluate(exchange));
        return ExchangeHelper.SleepAsync(delay, exchange.Context.TimeProvider, AsyncDelayed, exchange.CancellationToken);
    }

    /// <inheritdoc />
    public override string ToString() => $"Delay[{Delay}]";
}
