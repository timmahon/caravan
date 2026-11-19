using Caravan.Model;

namespace Caravan.Builder;

/// <summary>DSL for the <c>circuitBreaker</c> EIP.</summary>
public static class CircuitBreakerDsl
{
    /// <summary>
    /// Runs <paramref name="block"/> through a circuit breaker (Polly):
    /// <c>.CircuitBreaker(cb =&gt; cb.To("direct:remote"), c =&gt; c.FailureRatio(0.5).MinimumThroughput(10).Timeout(TimeSpan.FromSeconds(1)), fallback: f =&gt; f.SetBody(_ =&gt; "cached"))</c>.
    /// The fallback runs when the circuit is open, the block fails, times out or the bulkhead rejects the call; without one the failure propagates.
    /// The route's error handler does not apply inside the breaker (see <see cref="CircuitBreakerDefinition"/>).
    /// </summary>
    /// <param name="steps">The block to add to.</param>
    /// <param name="block">The protected steps.</param>
    /// <param name="configure">Configures the breaker (thresholds, time limit, bulkhead, shared configuration).</param>
    /// <param name="fallback">The fallback steps (Camel's <c>onFallback</c>).</param>
    public static T CircuitBreaker<T>(this T steps, Action<RouteSteps> block, Action<CircuitBreakerBuilder>? configure = null, Action<RouteSteps>? fallback = null)
        where T : RouteSteps
    {
        ArgumentNullException.ThrowIfNull(block);
        var definition = steps.AddStep(new CircuitBreakerDefinition());
        steps.BuildNested(definition.Outputs, definition, block);
        var builder = new CircuitBreakerBuilder(steps, definition);
        configure?.Invoke(builder);
        if (fallback is not null)
        {
            builder.OnFallback(fallback);
        }

        return steps;
    }
}
