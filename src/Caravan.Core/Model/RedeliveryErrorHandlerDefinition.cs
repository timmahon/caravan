using Caravan.Processors;
using Caravan.Spi;

namespace Caravan.Model;

/// <summary>
/// Base of the error handlers that redeliver (Camel's <c>RedeliveryErrorHandler</c>): <see cref="DefaultErrorHandlerDefinition"/>
/// and <see cref="DeadLetterChannelDefinition"/>. Each node of the route is wrapped, so a redelivery retries only the failing node.
/// Configure it with the extension methods of <c>Caravan.Builder.ErrorHandlerDsl</c> and <c>Caravan.Builder.RedeliveryDsl</c>.
/// </summary>
public abstract class RedeliveryErrorHandlerDefinition : ErrorHandlerDefinition, IRedeliveryPolicyAware
{
    /// <inheritdoc />
    public RedeliveryPolicyDefinition RedeliveryPolicy { get; } = new();

    /// <summary>Called before each redelivery attempt (after the delay), e.g. to change the message.</summary>
    public IProcessor? OnRedelivery { get; set; }

    /// <summary>Called right after each exception occurs, before the error handler decides what to do. Its failures are logged and ignored.</summary>
    public IProcessor? OnExceptionOccurred { get; set; }

    /// <summary>Called to prepare the exchange before it is handed to the failure processor (dead letter channel or <c>onException</c> block).</summary>
    public IProcessor? OnPrepareFailure { get; set; }

    /// <summary>When set, redelivery continues while it matches (instead of using the maximum redeliveries).</summary>
    public IPredicate? RetryWhile { get; set; }

    /// <summary>Hands the original message (as received by the route's consumer) to the failure processor instead of the current one.</summary>
    public bool UseOriginalMessage { get; set; }

    /// <summary>Hands the original body to the failure processor, keeping the current headers.</summary>
    public bool UseOriginalBody { get; set; }

    /// <summary>The logger category; defaults to the error handler's name.</summary>
    public string? LoggerName { get; set; }

    /// <summary>The URI of the dead letter endpoint, or null when the error handler has none.</summary>
    public abstract string? DeadLetterUri { get; }

    /// <summary>Whether a new exception thrown while sending to the dead letter endpoint is handled too (dead letter channel only).</summary>
    public virtual bool DeadLetterHandleNewException => true;

    /// <summary>The name used in logs, e.g. <c>DefaultErrorHandler</c>.</summary>
    public abstract string Name { get; }

    /// <inheritdoc />
    public override bool RequiresOriginalMessage => UseOriginalMessage || UseOriginalBody;

    /// <inheritdoc />
    public override IErrorHandlerFactory? CreateFactory(RouteContext routeContext) => new RedeliveryErrorHandlerFactory(routeContext, this);

    /// <inheritdoc />
    public override string ToString() => Name;
}
