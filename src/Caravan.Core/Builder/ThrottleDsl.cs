using Caravan.Model;

namespace Caravan.Builder;

/// <summary>DSL for the <c>throttle</c> EIP.</summary>
public static class ThrottleDsl
{
    /// <summary>
    /// Limits the exchanges passing through to <paramref name="maxRequests"/>: in flight at a time by default
    /// (<see cref="ThrottlingMode.ConcurrentRequests"/>), or per time period (<c>t =&gt; t.TimePeriod(TimeSpan.FromSeconds(1))</c>,
    /// <see cref="ThrottlingMode.TotalRequests"/>). Exchanges over the limit wait, or fail with <c>t.RejectExecution()</c>.
    /// </summary>
    /// <param name="steps">The block to add to.</param>
    /// <param name="maxRequests">The maximum number of requests.</param>
    /// <param name="configure">Configures the options.</param>
    public static T Throttle<T>(this T steps, long maxRequests, Action<ThrottleBuilder>? configure = null)
        where T : RouteSteps => steps.Throttle(Expr.Constant(maxRequests), configure);

    /// <summary>Limits the exchanges passing through to the value of <paramref name="maxRequests"/>, evaluated for each exchange.</summary>
    /// <param name="steps">The block to add to.</param>
    /// <param name="maxRequests">Computes the maximum number of requests.</param>
    /// <param name="configure">Configures the options.</param>
    public static T Throttle<T>(this T steps, IExpression maxRequests, Action<ThrottleBuilder>? configure = null)
        where T : RouteSteps
    {
        var definition = steps.AddStep(new ThrottleDefinition(maxRequests));
        configure?.Invoke(new ThrottleBuilder(definition));
        return steps;
    }

    /// <summary>Limits the exchanges passing through to the value returned by <paramref name="maxRequests"/>, evaluated for each exchange.</summary>
    /// <param name="steps">The block to add to.</param>
    /// <param name="maxRequests">Computes the maximum number of requests.</param>
    /// <param name="configure">Configures the options.</param>
    public static T Throttle<T>(this T steps, Func<Exchange, object?> maxRequests, Action<ThrottleBuilder>? configure = null)
        where T : RouteSteps => steps.Throttle(new FuncExpression(maxRequests), configure);
}
