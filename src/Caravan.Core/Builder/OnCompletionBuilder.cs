using Caravan.Model;

namespace Caravan.Builder;

/// <summary>Builds an <c>onCompletion</c> block: <c>.OnCompletion(c =&gt; c.OnFailureOnly().To("mock:failed"))</c>.</summary>
public sealed class OnCompletionBuilder : RouteSteps
{
    /// <summary>Creates a builder for <paramref name="definition"/>.</summary>
    public OnCompletionBuilder(OnCompletionDefinition definition, RouteDefinition? route = null)
        : base(definition.Outputs, route, definition)
    {
        Definition = definition;
    }

    /// <summary>The block being built.</summary>
    public OnCompletionDefinition Definition { get; }

    /// <summary>Runs only when the exchange completed successfully.</summary>
    public OnCompletionBuilder OnCompleteOnly()
    {
        Definition.OnCompleteOnly = true;
        Definition.OnFailureOnly = false;
        return this;
    }

    /// <summary>Runs only when the exchange failed.</summary>
    public OnCompletionBuilder OnFailureOnly()
    {
        Definition.OnFailureOnly = true;
        Definition.OnCompleteOnly = false;
        return this;
    }

    /// <summary>Runs only when the predicate matches.</summary>
    public OnCompletionBuilder OnWhen(IPredicate predicate)
    {
        Definition.OnWhen = predicate;
        return this;
    }

    /// <summary>Runs only when the predicate matches.</summary>
    public OnCompletionBuilder OnWhen(Func<Exchange, bool> predicate) => OnWhen(Pred.From(predicate));

    /// <summary>Runs when the route has finished, before the reply goes back to the consumer, on the exchange itself.</summary>
    public OnCompletionBuilder ModeBeforeConsumer()
    {
        Definition.Mode = OnCompletionMode.BeforeConsumer;
        return this;
    }

    /// <summary>Runs after the exchange completed, on a copy (the default).</summary>
    public OnCompletionBuilder ModeAfterConsumer()
    {
        Definition.Mode = OnCompletionMode.AfterConsumer;
        return this;
    }

    /// <summary>Runs the block on a copy in the background, without waiting for it.</summary>
    public OnCompletionBuilder ParallelProcessing(bool parallel = true)
    {
        Definition.ParallelProcessing = parallel;
        return this;
    }

    /// <summary>Runs the block on the original message (as received by the route's consumer).</summary>
    public OnCompletionBuilder UseOriginalMessage()
    {
        Definition.UseOriginalMessage = true;
        return this;
    }
}
