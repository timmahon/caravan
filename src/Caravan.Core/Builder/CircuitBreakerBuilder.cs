using Caravan.Model;

namespace Caravan.Builder;

/// <summary>
/// Options of the <c>circuitBreaker</c> EIP:
/// <c>c =&gt; c.FailureRatio(0.5).MinimumThroughput(10).SamplingDuration(...).BreakDuration(...).Timeout(...).Bulkhead(10, 5).OnFallback(f =&gt; ...)</c>.
/// </summary>
public sealed class CircuitBreakerBuilder
{
    private readonly RouteSteps _steps;

    /// <summary>Creates the builder for <paramref name="definition"/>.</summary>
    /// <param name="steps">The block containing the breaker (used to build the fallback block).</param>
    /// <param name="definition">The definition being configured.</param>
    public CircuitBreakerBuilder(RouteSteps steps, CircuitBreakerDefinition definition)
    {
        _steps = steps;
        Definition = definition;
    }

    /// <summary>The definition being configured.</summary>
    public CircuitBreakerDefinition Definition { get; }

    private CircuitBreakerConfiguration Configuration => Definition.Configuration;

    /// <summary>Starts from the named shared configuration (see <see cref="CircuitBreakerDefinition.ConfigurationRef"/>); options set here override it.</summary>
    /// <param name="name">The configuration's name.</param>
    public CircuitBreakerBuilder ConfigurationRef(string name)
    {
        Definition.ConfigurationRef = name;
        return this;
    }

    /// <summary>The ratio of failed calls (0 to 1] at which the circuit opens (default 0.5).</summary>
    /// <param name="ratio">The ratio.</param>
    public CircuitBreakerBuilder FailureRatio(double ratio)
    {
        Configuration.FailureRatio = ratio;
        return this;
    }

    /// <summary>The minimum number of calls within the sampling duration before the circuit can open (default 100, at least 2).</summary>
    /// <param name="calls">The number of calls.</param>
    public CircuitBreakerBuilder MinimumThroughput(int calls)
    {
        Configuration.MinimumThroughput = calls;
        return this;
    }

    /// <summary>The window over which failures are counted (default 30 s, at least 500 ms).</summary>
    /// <param name="duration">The duration.</param>
    public CircuitBreakerBuilder SamplingDuration(TimeSpan duration)
    {
        Configuration.SamplingDuration = duration;
        return this;
    }

    /// <summary>How long the circuit stays open before a probe call is let through (default 60 s, at least 500 ms).</summary>
    /// <param name="duration">The duration.</param>
    public CircuitBreakerBuilder BreakDuration(TimeSpan duration)
    {
        Configuration.BreakDuration = duration;
        return this;
    }

    /// <summary>The maximum time a call may take; slower calls time out and count as failures (<see cref="TimeSpan.Zero"/> disables it).</summary>
    /// <param name="timeout">The time limit.</param>
    public CircuitBreakerBuilder Timeout(TimeSpan timeout)
    {
        Configuration.Timeout = timeout;
        return this;
    }

    /// <summary>
    /// Limits the calls running at a time to <paramref name="maxConcurrentCalls"/>, with up to <paramref name="maxWaitingCalls"/> waiting for a
    /// slot; further calls are rejected (<see cref="BulkheadRejectedException"/>).
    /// </summary>
    /// <param name="maxConcurrentCalls">The maximum number of concurrent calls (0 disables the bulkhead).</param>
    /// <param name="maxWaitingCalls">The maximum number of waiting calls.</param>
    public CircuitBreakerBuilder Bulkhead(int maxConcurrentCalls, int maxWaitingCalls = 0)
    {
        Configuration.BulkheadMaxConcurrentCalls = maxConcurrentCalls;
        Configuration.BulkheadMaxWaitingCalls = maxWaitingCalls;
        return this;
    }

    /// <summary>Only these exceptions (and subtypes, also as inner exceptions) count as failures of the circuit.</summary>
    /// <param name="exceptionTypes">The exception types.</param>
    public CircuitBreakerBuilder RecordExceptions(params Type[] exceptionTypes)
    {
        Configuration.RecordExceptions = [.. exceptionTypes];
        return this;
    }

    /// <summary>These exceptions (and subtypes, also as inner exceptions) do not count as failures of the circuit.</summary>
    /// <param name="exceptionTypes">The exception types.</param>
    public CircuitBreakerBuilder IgnoreExceptions(params Type[] exceptionTypes)
    {
        Configuration.IgnoreExceptions = [.. exceptionTypes];
        return this;
    }

    /// <summary>Applies the route's error handler (redelivery, <c>onException</c>) inside the breaker (default false).</summary>
    /// <param name="inherit">Whether to inherit the error handler.</param>
    public CircuitBreakerBuilder InheritErrorHandler(bool inherit = true)
    {
        Definition.InheritErrorHandler = inherit;
        return this;
    }

    /// <summary>Sets the fallback block (Camel's <c>onFallback</c>).</summary>
    /// <param name="block">The fallback steps.</param>
    public CircuitBreakerBuilder OnFallback(Action<RouteSteps> block) => SetFallback(block, viaNetwork: false);

    /// <summary>
    /// Sets a fallback block that calls a remote service: it runs through a circuit breaker of its own (Camel's <c>onFallbackViaNetwork</c>).
    /// </summary>
    /// <param name="block">The fallback steps.</param>
    public CircuitBreakerBuilder OnFallbackViaNetwork(Action<RouteSteps> block) => SetFallback(block, viaNetwork: true);

    /// <exception cref="InvalidOperationException">A fallback was already set.</exception>
    private CircuitBreakerBuilder SetFallback(Action<RouteSteps> block, bool viaNetwork)
    {
        ArgumentNullException.ThrowIfNull(block);
        if (Definition.OnFallback is not null)
        {
            throw new InvalidOperationException("A circuitBreaker can only have one fallback");
        }

        var fallback = new OnFallbackDefinition { FallbackViaNetwork = viaNetwork, Parent = Definition, Route = _steps.Route };
        Definition.OnFallback = fallback;
        _steps.BuildNested(fallback.Outputs, fallback, block);
        return this;
    }
}
