namespace Caravan;

/// <summary>A route failed to start.</summary>
public class FailedToStartRouteException(string routeId, string message, Exception? innerException = null)
    : CaravanException($"Failed to start route {routeId} because of {message}", innerException)
{
    /// <summary>The id of the route.</summary>
    public string RouteId { get; } = routeId;
}
