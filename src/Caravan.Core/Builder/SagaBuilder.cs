using Caravan.Model;
using Caravan.Saga;

namespace Caravan.Builder;

/// <summary>
/// Options of the <c>saga</c> EIP:
/// <c>s =&gt; s.Propagation(SagaPropagation.RequiresNew).CompletionMode(SagaCompletionMode.Manual).Compensation("direct:undo").Completion("direct:done").Timeout(TimeSpan.FromMinutes(1)).Option("id", Expr.Header("orderId"))</c>.
/// </summary>
public sealed class SagaBuilder
{
    /// <summary>Creates the builder for <paramref name="definition"/>.</summary>
    /// <param name="definition">The definition being configured.</param>
    public SagaBuilder(SagaDefinition definition)
    {
        Definition = definition;
    }

    /// <summary>The definition being configured.</summary>
    public SagaDefinition Definition { get; }

    /// <summary>How the EIP relates to the current saga (default <see cref="SagaPropagation.Required"/>).</summary>
    /// <param name="propagation">The propagation.</param>
    public SagaBuilder Propagation(SagaPropagation propagation)
    {
        Definition.Propagation = propagation;
        return this;
    }

    /// <summary>When a saga started by the EIP ends (default <see cref="SagaCompletionMode.Auto"/>).</summary>
    /// <param name="mode">The completion mode.</param>
    public SagaBuilder CompletionMode(SagaCompletionMode mode)
    {
        Definition.CompletionMode = mode;
        return this;
    }

    /// <summary>The endpoint called when the saga is compensated.</summary>
    /// <param name="uri">The endpoint URI.</param>
    public SagaBuilder Compensation(string uri)
    {
        Definition.CompensationUri = uri;
        return this;
    }

    /// <summary>The endpoint called when the saga completes.</summary>
    /// <param name="uri">The endpoint URI.</param>
    public SagaBuilder Completion(string uri)
    {
        Definition.CompletionUri = uri;
        return this;
    }

    /// <summary>Compensates the saga when it is still running this long after the step began.</summary>
    /// <param name="timeout">The timeout.</param>
    public SagaBuilder Timeout(TimeSpan timeout)
    {
        Definition.Timeout = timeout;
        return this;
    }

    /// <summary>A value evaluated when the step begins and sent as the header <paramref name="name"/> to the compensation and completion endpoints.</summary>
    /// <param name="name">The header name.</param>
    /// <param name="expression">Computes the value.</param>
    public SagaBuilder Option(string name, IExpression expression)
    {
        Definition.Options[name] = expression;
        return this;
    }

    /// <summary>A value evaluated when the step begins and sent as the header <paramref name="name"/> to the compensation and completion endpoints.</summary>
    /// <param name="name">The header name.</param>
    /// <param name="expression">Computes the value.</param>
    public SagaBuilder Option(string name, Func<Exchange, object?> expression) => Option(name, new FuncExpression(expression));

    /// <summary>Uses <paramref name="service"/> instead of the context's saga service.</summary>
    /// <param name="service">The saga service.</param>
    public SagaBuilder SagaService(ISagaService service)
    {
        Definition.SagaService = service;
        return this;
    }
}
