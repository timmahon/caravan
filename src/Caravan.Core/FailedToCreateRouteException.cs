namespace Caravan;

/// <summary>A route definition is invalid.</summary>
public class FailedToCreateRouteException(string routeId, string message, Exception? innerException = null)
    : CaravanException($"Failed to create route {routeId}: {message}", innerException)
{
    /// <summary>The id of the route.</summary>
    public string RouteId { get; } = routeId;
}
