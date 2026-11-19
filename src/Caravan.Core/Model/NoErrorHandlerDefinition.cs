using Caravan.Spi;

namespace Caravan.Model;

/// <summary>
/// Camel's <c>noErrorHandler</c>: nodes are not wrapped, there is no redelivery and <c>onException</c> clauses do not apply.
/// Failures propagate to the caller, so a route called through <c>direct</c> lets the calling route's error handler deal with them.
/// </summary>
public sealed class NoErrorHandlerDefinition : ErrorHandlerDefinition
{
    /// <inheritdoc />
    public override IErrorHandlerFactory? CreateFactory(RouteContext routeContext) => null;

    /// <inheritdoc />
    public override string ToString() => "NoErrorHandler";
}
