using Caravan.Spi;

namespace Caravan.Model;

/// <summary>
/// An <c>onCompletion</c> block: steps run when an exchange routed by the route completes (successfully, or failed with
/// <see cref="OnFailureOnly"/>). Blocks defined on a route builder apply to all its routes that have none of their own. Each block
/// runs at most once per exchange, even when the exchange passes through several routes using it.
/// </summary>
public sealed class OnCompletionDefinition : OutputDefinition
{
    /// <summary>When the block runs (default <see cref="OnCompletionMode.AfterConsumer"/>).</summary>
    public OnCompletionMode Mode { get; set; }

    /// <summary>Runs only when the exchange completed successfully.</summary>
    public bool OnCompleteOnly { get; set; }

    /// <summary>Runs only when the exchange failed (or was marked rollback-only).</summary>
    public bool OnFailureOnly { get; set; }

    /// <summary>An extra condition for the block to run.</summary>
    public IPredicate? OnWhen { get; set; }

    /// <summary>Runs the block on a copy in the background, without waiting for it.</summary>
    public bool ParallelProcessing { get; set; }

    /// <summary>Runs the block on the original message (as received by the route's consumer).</summary>
    public bool UseOriginalMessage { get; set; }

    /// <summary>True for a block defined on a route; false for one defined on a route builder.</summary>
    public bool RouteScoped { get; set; }

    /// <inheritdoc />
    public override string ShortName => "onCompletion";

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">Both <see cref="OnCompleteOnly"/> and <see cref="OnFailureOnly"/> are set.</exception>
    public override IProcessor CreateProcessor(RouteContext routeContext)
    {
        if (OnCompleteOnly && OnFailureOnly)
        {
            throw new InvalidOperationException("onCompletion cannot be both onCompleteOnly and onFailureOnly");
        }

        return routeContext.CreateBlockProcessor(Outputs, this);
    }
}
