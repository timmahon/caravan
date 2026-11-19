using Caravan.Impl;
using Caravan.Processors;
using Caravan.Spi;

namespace Caravan.Model;

/// <summary>
/// The <c>circuitBreaker</c> EIP (Camel's <c>circuitBreaker</c> with Resilience4j), built on Polly v8: runs <see cref="OutputDefinition.Outputs"/>
/// through a circuit breaker (with an optional time limit and bulkhead) and runs <see cref="OnFallback"/> when the circuit is open, the call fails,
/// times out or is rejected. See <see cref="CircuitBreakerProcessor"/>.
/// </summary>
/// <remarks>
/// As in Camel the breaker handles failures itself: unless <see cref="InheritErrorHandler"/> is set, the route's error handler (redelivery,
/// <c>onException</c>) is not applied to the nodes inside the block nor the fallback. A failure that leaves the breaker (no fallback, or a failing
/// fallback) goes to the error handler of the <c>circuitBreaker</c> node without redelivery: <c>onException</c> or the dead letter channel still
/// deal with it, but the call is not repeated.
/// </remarks>
public sealed class CircuitBreakerDefinition : OutputDefinition
{
    /// <summary>The breaker's own options (they override the referenced and the context's default configuration).</summary>
    public CircuitBreakerConfiguration Configuration { get; set; } = new();

    /// <summary>
    /// The name of a shared configuration: one added with <see cref="CircuitBreakerContextExtensions.AddCircuitBreakerConfiguration"/>, or a
    /// <see cref="CircuitBreakerConfiguration"/> bean in the registry (Camel's <c>configuration</c> reference).
    /// </summary>
    public string? ConfigurationRef { get; set; }

    /// <summary>The fallback block, if any; without one a failure propagates.</summary>
    public OnFallbackDefinition? OnFallback { get; set; }

    /// <summary>Whether the route's error handler applies inside the block (Camel's <c>inheritErrorHandler</c>, default false).</summary>
    public bool InheritErrorHandler { get; set; }

    /// <inheritdoc />
    public override string ShortName => "circuitBreaker";

    /// <inheritdoc />
    public override IEnumerable<ProcessorDefinition> Children => OnFallback is null ? Outputs : [.. Outputs, OnFallback];

    /// <summary>
    /// The effective configuration: the defaults, overridden by the context's default configuration, the referenced one and the breaker's own
    /// options.
    /// </summary>
    /// <param name="context">The context holding the shared configurations.</param>
    /// <returns>The merged configuration.</returns>
    /// <exception cref="NoSuchBeanException">The referenced configuration does not exist.</exception>
    public CircuitBreakerConfiguration ResolveConfiguration(ICaravanContext context)
    {
        var configurations = CircuitBreakerContextExtensions.Configurations(context);
        CircuitBreakerConfiguration? referenced = null;
        if (ConfigurationRef is { } name)
        {
            referenced = configurations.Named.TryGetValue(name, out var named)
                ? named
                : Support.RegistryLookup.Lookup<CircuitBreakerConfiguration>(context, name);
        }

        return new CircuitBreakerConfiguration().OverrideWith(configurations.Default).OverrideWith(referenced).OverrideWith(Configuration);
    }

    /// <inheritdoc />
    public override IProcessor CreateProcessor(RouteContext routeContext)
    {
        var context = routeContext.Context;
        var id = Id ??= context.NextNodeId(ShortName);
        var configuration = ResolveConfiguration(context);
        var block = routeContext.CreateBlockProcessor(Outputs, this);
        IProcessor? fallback = null;
        CircuitBreakerCircuit? fallbackCircuit = null;
        if (OnFallback is { Disabled: false } onFallback)
        {
            onFallback.Parent = this;
            onFallback.Route ??= routeContext.Definition;
            onFallback.Id ??= context.NextNodeId(onFallback.ShortName);
            fallback = onFallback.CreateProcessor(routeContext);
            if (onFallback.FallbackViaNetwork)
            {
                fallbackCircuit = new CircuitBreakerCircuit(id + "-fallback", configuration, context.TimeProvider);
            }
        }

        var circuit = new CircuitBreakerCircuit(id, configuration, context.TimeProvider);
        var registry = (DefaultCircuitBreakerStateRegistry)context.GetCircuitBreakerStateRegistry();
        registry.Register(circuit.Id, circuit);
        if (fallbackCircuit is not null)
        {
            registry.Register(fallbackCircuit.Id, fallbackCircuit);
        }

        return new CircuitBreakerProcessor(circuit, block, fallback, fallbackCircuit, configuration, InheritErrorHandler);
    }
}
