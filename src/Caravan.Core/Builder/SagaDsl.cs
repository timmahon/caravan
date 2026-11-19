using Caravan.Model;

namespace Caravan.Builder;

/// <summary>DSL for the <c>saga</c> EIP and the <c>saga</c> component.</summary>
public static class SagaDsl
{
    /// <summary>
    /// Runs <paramref name="body"/> as part of a saga:
    /// <c>.Saga(s =&gt; s.Compensation("direct:cancelOrder").Completion("direct:confirmOrder").Option("id", Expr.Header("orderId")), body =&gt; body.To("direct:reserve"))</c>.
    /// A saga started here (default <see cref="SagaPropagation.Required"/> without a current saga, or <see cref="SagaPropagation.RequiresNew"/>)
    /// is completed when the block succeeds and compensated when it fails (<see cref="SagaCompletionMode.Auto"/>).
    /// </summary>
    /// <param name="steps">The block to add to.</param>
    /// <param name="configure">Configures propagation, completion mode, endpoints, options and timeout.</param>
    /// <param name="body">The steps taking part in the saga.</param>
    public static T Saga<T>(this T steps, Action<SagaBuilder> configure, Action<RouteSteps> body)
        where T : RouteSteps
    {
        ArgumentNullException.ThrowIfNull(body);
        var definition = steps.AddStep(new SagaDefinition());
        configure?.Invoke(new SagaBuilder(definition));
        steps.BuildNested(definition.Outputs, definition, body);
        return steps;
    }

    /// <summary>
    /// Makes the rest of the exchange part of a saga (Camel's chained <c>from(...).saga()...</c>): a saga started here in
    /// <see cref="SagaCompletionMode.Auto"/> mode is completed or compensated when the exchange's unit of work completes.
    /// </summary>
    /// <param name="steps">The block to add to.</param>
    /// <param name="configure">Configures propagation, completion mode, endpoints, options and timeout.</param>
    public static T Saga<T>(this T steps, Action<SagaBuilder>? configure = null)
        where T : RouteSteps
    {
        var definition = steps.AddStep(new SagaDefinition { ScopeIsRestOfRoute = true });
        configure?.Invoke(new SagaBuilder(definition));
        return steps;
    }

    /// <summary>Completes the exchange's saga (sends to <c>saga:complete</c>).</summary>
    /// <param name="steps">The block to add to.</param>
    public static T SagaComplete<T>(this T steps)
        where T : RouteSteps => steps.To("saga:complete");

    /// <summary>Compensates the exchange's saga (sends to <c>saga:compensate</c>).</summary>
    /// <param name="steps">The block to add to.</param>
    public static T SagaCompensate<T>(this T steps)
        where T : RouteSteps => steps.To("saga:compensate");
}
