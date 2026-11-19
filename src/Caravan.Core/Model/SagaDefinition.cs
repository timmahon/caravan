using Caravan.Processors;
using Caravan.Saga;
using Caravan.Spi;

namespace Caravan.Model;

/// <summary>
/// The <c>saga</c> EIP (Camel's <c>saga</c>, LRA style): makes the exchange take part in a saga according to <see cref="Propagation"/> and
/// registers a step with <see cref="CompensationUri"/> and <see cref="CompletionUri"/>, which the saga service calls when the saga is compensated
/// or completed. See <see cref="SagaProcessor"/>.
/// </summary>
/// <remarks>
/// With a block (<see cref="OutputDefinition.Outputs"/>, <see cref="ScopeIsRestOfRoute"/> false) the saga scope is the block: a saga the EIP
/// started in <see cref="SagaCompletionMode.Auto"/> mode is completed or compensated when the block ends, and the saga header is restored. Without
/// a block (<see cref="ScopeIsRestOfRoute"/> true, Camel's chained form <c>from(...).saga()...</c>) the scope is the rest of the exchange: the
/// saga ends when the exchange's unit of work completes.
/// </remarks>
public sealed class SagaDefinition : OutputDefinition
{
    /// <summary>How the EIP relates to the current saga (default <see cref="SagaPropagation.Required"/>).</summary>
    public SagaPropagation Propagation { get; set; } = SagaPropagation.Required;

    /// <summary>When a saga started by the EIP ends (default <see cref="SagaCompletionMode.Auto"/>).</summary>
    public SagaCompletionMode CompletionMode { get; set; } = SagaCompletionMode.Auto;

    /// <summary>The endpoint called when the saga is compensated, or null.</summary>
    public string? CompensationUri { get; set; }

    /// <summary>The endpoint called when the saga completes, or null.</summary>
    public string? CompletionUri { get; set; }

    /// <summary>When set, the saga is compensated if it is still running this long after the step began.</summary>
    public TimeSpan? Timeout { get; set; }

    /// <summary>Values evaluated when the step begins and sent as headers to the compensation and completion endpoints.</summary>
    public IDictionary<string, IExpression> Options { get; } = new Dictionary<string, IExpression>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Whether the saga covers the rest of the exchange rather than the nested block (the chained form, without a block).</summary>
    public bool ScopeIsRestOfRoute { get; set; }

    /// <summary>The saga service to use instead of the context's (<see cref="SagaContextExtensions.GetSagaService"/>).</summary>
    public ISagaService? SagaService { get; set; }

    /// <inheritdoc />
    public override string ShortName => "saga";

    /// <inheritdoc />
    public override string Label => $"saga[{Propagation}]";

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">The chained form has nested steps.</exception>
    public override IProcessor CreateProcessor(RouteContext routeContext)
    {
        if (ScopeIsRestOfRoute && Outputs.Count > 0)
        {
            throw new InvalidOperationException("saga: a saga covering the rest of the route cannot have nested steps");
        }

        var step = new SagaStep(
            CompensationUri is null ? null : routeContext.ResolveEndpoint(CompensationUri),
            CompletionUri is null ? null : routeContext.ResolveEndpoint(CompletionUri),
            new Dictionary<string, IExpression>(Options, StringComparer.OrdinalIgnoreCase),
            Timeout);
        var block = ScopeIsRestOfRoute ? null : routeContext.CreateBlockProcessor(Outputs, this);
        return new SagaProcessor(routeContext.Context, SagaService ?? routeContext.Context.GetSagaService(), block, Propagation, CompletionMode, step);
    }
}
