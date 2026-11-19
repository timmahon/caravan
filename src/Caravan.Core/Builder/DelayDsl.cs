using Caravan.Model;

namespace Caravan.Builder;

/// <summary>DSL for the <c>delay</c> EIP.</summary>
public static class DelayDsl
{
    /// <summary>Delays the exchange by <paramref name="delay"/> on the context's <see cref="ICaravanContext.TimeProvider"/>: <c>.Delay(TimeSpan.FromSeconds(1))</c>.</summary>
    /// <param name="steps">The block to add to.</param>
    /// <param name="delay">The delay.</param>
    /// <param name="asyncDelayed">Wait without blocking the calling thread (default); false blocks it, as Camel's synchronous delayer.</param>
    /// <param name="callerRunsWhenRejected">Accepted for parity with Camel; has no effect.</param>
    public static T Delay<T>(this T steps, TimeSpan delay, bool asyncDelayed = true, bool callerRunsWhenRejected = true)
        where T : RouteSteps => steps.Delay(Expr.Constant(delay), asyncDelayed, callerRunsWhenRejected);

    /// <summary>Delays the exchange by the value of <paramref name="delay"/>: a <see cref="TimeSpan"/> or a number of milliseconds (null, zero or negative: no delay).</summary>
    /// <param name="steps">The block to add to.</param>
    /// <param name="delay">Computes the delay.</param>
    /// <param name="asyncDelayed">Wait without blocking the calling thread (default); false blocks it, as Camel's synchronous delayer.</param>
    /// <param name="callerRunsWhenRejected">Accepted for parity with Camel; has no effect.</param>
    public static T Delay<T>(this T steps, IExpression delay, bool asyncDelayed = true, bool callerRunsWhenRejected = true)
        where T : RouteSteps
    {
        steps.AddStep(new DelayDefinition(delay) { AsyncDelayed = asyncDelayed, CallerRunsWhenRejected = callerRunsWhenRejected });
        return steps;
    }

    /// <summary>Delays the exchange by the value returned by <paramref name="delay"/>: a <see cref="TimeSpan"/> or a number of milliseconds.</summary>
    /// <param name="steps">The block to add to.</param>
    /// <param name="delay">Computes the delay.</param>
    /// <param name="asyncDelayed">Wait without blocking the calling thread (default); false blocks it, as Camel's synchronous delayer.</param>
    /// <param name="callerRunsWhenRejected">Accepted for parity with Camel; has no effect.</param>
    public static T Delay<T>(this T steps, Func<Exchange, object?> delay, bool asyncDelayed = true, bool callerRunsWhenRejected = true)
        where T : RouteSteps => steps.Delay(new FuncExpression(delay), asyncDelayed, callerRunsWhenRejected);
}
