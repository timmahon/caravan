namespace Caravan.Model;

/// <summary>
/// Camel's <c>DefaultErrorHandler</c>, used when no error handler is configured: no redelivery unless configured, the failure is
/// logged at ERROR with the exchange and propagates to the caller. <c>onException</c> clauses apply.
/// </summary>
public sealed class DefaultErrorHandlerDefinition : RedeliveryErrorHandlerDefinition
{
    /// <inheritdoc />
    public override string? DeadLetterUri => null;

    /// <inheritdoc />
    public override string Name => "DefaultErrorHandler";
}
