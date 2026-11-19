using Caravan.Model;
using Caravan.Spi;

namespace Caravan.Builder;

/// <summary>
/// Builds an <c>onException</c> clause: its options and its steps, e.g.
/// <c>OnException&lt;IOException&gt;(o =&gt; o.Handled(true).MaximumRedeliveries(3).To("mock:error"))</c>.
/// Redelivery options come from <see cref="RedeliveryDsl"/> and override the error handler's.
/// </summary>
public sealed class OnExceptionBuilder : RouteSteps, IRedeliveryPolicyAware
{
    /// <summary>Creates a builder for <paramref name="definition"/>.</summary>
    public OnExceptionBuilder(OnExceptionDefinition definition, RouteDefinition? route = null)
        : base(definition.Outputs, route, definition)
    {
        Definition = definition;
    }

    /// <summary>The clause being built.</summary>
    public OnExceptionDefinition Definition { get; }

    /// <inheritdoc />
    public RedeliveryPolicyDefinition RedeliveryPolicy => Definition.RedeliveryPolicy;

    /// <summary>Whether the failure is handled: the caller sees success with the clause's result and the route stops.</summary>
    public OnExceptionBuilder Handled(bool handled) => Handled(Pred.Constant(handled));

    /// <summary>Handles the failure when the predicate matches.</summary>
    public OnExceptionBuilder Handled(IPredicate predicate)
    {
        Definition.Handled = predicate;
        return this;
    }

    /// <summary>Handles the failure when the predicate matches.</summary>
    public OnExceptionBuilder Handled(Func<Exchange, bool> predicate) => Handled(Pred.From(predicate));

    /// <summary>Whether the exception is ignored and routing continues with the next node.</summary>
    public OnExceptionBuilder Continued(bool continued) => Continued(Pred.Constant(continued));

    /// <summary>Continues routing when the predicate matches.</summary>
    public OnExceptionBuilder Continued(IPredicate predicate)
    {
        Definition.Continued = predicate;
        return this;
    }

    /// <summary>Continues routing when the predicate matches.</summary>
    public OnExceptionBuilder Continued(Func<Exchange, bool> predicate) => Continued(Pred.From(predicate));

    /// <summary>The clause only matches when the predicate does too.</summary>
    public OnExceptionBuilder OnWhen(IPredicate predicate)
    {
        Definition.OnWhen = predicate;
        return this;
    }

    /// <summary>The clause only matches when the predicate does too.</summary>
    public OnExceptionBuilder OnWhen(Func<Exchange, bool> predicate) => OnWhen(Pred.From(predicate));

    /// <summary>Redelivers while the predicate matches (instead of using the maximum redeliveries).</summary>
    public OnExceptionBuilder RetryWhile(IPredicate predicate)
    {
        Definition.RetryWhile = predicate;
        return this;
    }

    /// <summary>Redelivers while the predicate matches (instead of using the maximum redeliveries).</summary>
    public OnExceptionBuilder RetryWhile(Func<Exchange, bool> predicate) => RetryWhile(Pred.From(predicate));

    /// <summary>Called before each redelivery attempt.</summary>
    public OnExceptionBuilder OnRedelivery(IProcessor processor)
    {
        Definition.OnRedelivery = processor;
        return this;
    }

    /// <summary>Called before each redelivery attempt.</summary>
    public OnExceptionBuilder OnRedelivery(Action<Exchange> processor) => OnRedelivery(DelegateProcessor.From(processor));

    /// <summary>Called right after each matching exception occurs; its failures are logged and ignored.</summary>
    public OnExceptionBuilder OnExceptionOccurred(IProcessor processor)
    {
        Definition.OnExceptionOccurred = processor;
        return this;
    }

    /// <summary>Called right after each matching exception occurs; its failures are logged and ignored.</summary>
    public OnExceptionBuilder OnExceptionOccurred(Action<Exchange> processor) => OnExceptionOccurred(DelegateProcessor.From(processor));

    /// <summary>Runs the clause's steps on the original message (as received by the route's consumer).</summary>
    public OnExceptionBuilder UseOriginalMessage()
    {
        Definition.UseOriginalMessage = true;
        return this;
    }

    /// <summary>Runs the clause's steps on the original body with the current headers.</summary>
    public OnExceptionBuilder UseOriginalBody()
    {
        Definition.UseOriginalBody = true;
        return this;
    }
}
