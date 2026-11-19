using Caravan.Spi;

namespace Caravan.Model;

/// <summary>
/// Configures the error handler of routes (Camel's <c>ErrorHandlerFactory</c>/<c>errorHandler(...)</c>). Set it on the context
/// (<see cref="ICaravanContext.ErrorHandler"/>), on a route builder (applies to its routes) or on a route; the most specific wins.
/// Derive from it to plug in a custom <see cref="IErrorHandlerFactory"/>.
/// </summary>
public abstract class ErrorHandlerDefinition
{
    /// <summary>Whether the error handler needs the original message of exchanges (enables <see cref="ICaravanContext.AllowUseOriginalMessage"/>).</summary>
    public virtual bool RequiresOriginalMessage => false;

    /// <summary>
    /// Creates the factory that wraps the nodes of one route, or null to not wrap them at all. Called once per route, before the
    /// route's processors are built; <paramref name="routeContext"/> gives access to the route definition (its <c>onException</c> clauses).
    /// </summary>
    public abstract IErrorHandlerFactory? CreateFactory(RouteContext routeContext);
}
