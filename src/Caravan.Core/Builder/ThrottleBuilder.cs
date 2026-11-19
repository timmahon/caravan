using Caravan.Model;

namespace Caravan.Builder;

/// <summary>Options of the <c>throttle</c> EIP: <c>.Throttle(10, t =&gt; t.TimePeriod(TimeSpan.FromSeconds(1)).RejectExecution())</c>.</summary>
public sealed class ThrottleBuilder
{
    /// <summary>Creates the builder for <paramref name="definition"/>.</summary>
    /// <param name="definition">The definition being configured.</param>
    public ThrottleBuilder(ThrottleDefinition definition)
    {
        Definition = definition;
    }

    /// <summary>The definition being configured.</summary>
    public ThrottleDefinition Definition { get; }

    /// <summary>
    /// The throttling mode. Without it the mode is <see cref="ThrottlingMode.TotalRequests"/> when a <see cref="TimePeriod"/> is set,
    /// else <see cref="ThrottlingMode.ConcurrentRequests"/>.
    /// </summary>
    /// <param name="mode">The mode.</param>
    public ThrottleBuilder Mode(ThrottlingMode mode)
    {
        Definition.Mode = mode;
        return this;
    }

    /// <summary>The period of <see cref="ThrottlingMode.TotalRequests"/> (default 1 second); setting it selects that mode unless <see cref="Mode"/> says otherwise.</summary>
    /// <param name="period">The period.</param>
    public ThrottleBuilder TimePeriod(TimeSpan period)
    {
        Definition.TimePeriod = period;
        return this;
    }

    /// <summary>Throttles exchanges per key: each value of <paramref name="expression"/> has its own limit.</summary>
    /// <param name="expression">Computes the key.</param>
    public ThrottleBuilder CorrelationExpression(IExpression expression)
    {
        Definition.CorrelationExpression = expression;
        return this;
    }

    /// <summary>Throttles exchanges per key: each value of <paramref name="expression"/> has its own limit.</summary>
    /// <param name="expression">Computes the key.</param>
    public ThrottleBuilder CorrelationExpression(Func<Exchange, object?> expression) => CorrelationExpression(new FuncExpression(expression));

    /// <summary>Fails exchanges over the limit with a <see cref="ThrottlerRejectedExecutionException"/> instead of making them wait.</summary>
    /// <param name="reject">Whether to reject.</param>
    public ThrottleBuilder RejectExecution(bool reject = true)
    {
        Definition.RejectExecution = reject;
        return this;
    }

    /// <summary>Whether waiting exchanges do not block their thread (default true); false blocks it, as Camel's synchronous throttler.</summary>
    /// <param name="asyncDelayed">Whether to wait asynchronously.</param>
    public ThrottleBuilder AsyncDelayed(bool asyncDelayed = true)
    {
        Definition.AsyncDelayed = asyncDelayed;
        return this;
    }

    /// <summary>Accepted for parity with Camel; has no effect (there is no executor that could reject).</summary>
    /// <param name="callerRuns">The value.</param>
    public ThrottleBuilder CallerRunsWhenRejected(bool callerRuns = true)
    {
        Definition.CallerRunsWhenRejected = callerRuns;
        return this;
    }
}
