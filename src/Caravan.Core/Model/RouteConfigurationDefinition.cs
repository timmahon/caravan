namespace Caravan.Model;

/// <summary>
/// Reusable cross-cutting route configuration (Camel 3.12+'s <c>routeConfiguration</c>): error handler, <c>onException</c>, <c>onCompletion</c>
/// and interceptors shared by many routes, typically defined in a <see cref="Builder.RouteConfigurationBuilder"/>. A route uses the
/// configurations whose id matches one of its <see cref="RouteDefinition.RouteConfigurationIds"/>; a route without ids uses the configurations
/// without id. Configurations are resolved when a route is created; see <see cref="Spi.RouteContext"/> for the precedence rules.
/// </summary>
public sealed class RouteConfigurationDefinition
{
    /// <summary>The id; null makes it a default configuration, applied to the routes that select none.</summary>
    public string? Id { get; set; }

    /// <summary>A description.</summary>
    public string? Description { get; set; }

    /// <summary>The error handler of the routes that set none themselves and whose builder sets none.</summary>
    public ErrorHandlerDefinition? ErrorHandler { get; set; }

    /// <summary>The <c>onException</c> clauses (global scope: after the route's own and its builder's).</summary>
    public List<OnExceptionDefinition> OnExceptions { get; } = [];

    /// <summary>The <c>onCompletion</c> blocks, used by routes that have none of their own or from their builder.</summary>
    public List<OnCompletionDefinition> OnCompletions { get; } = [];

    /// <summary>The <c>intercept</c> blocks (after the route's own).</summary>
    public List<InterceptDefinition> Intercepts { get; } = [];

    /// <summary>The <c>interceptFrom</c> blocks (after the route's own).</summary>
    public List<InterceptFromDefinition> InterceptFroms { get; } = [];

    /// <summary>The <c>interceptSendToEndpoint</c> definitions, registered in the context when a route using the configuration is created.</summary>
    public List<InterceptSendToEndpointDefinition> InterceptSendToEndpoints { get; } = [];

    /// <inheritdoc />
    public override string ToString() => $"RouteConfiguration({Id ?? "<default>"})";
}
