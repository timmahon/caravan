namespace Caravan;

/// <summary>A route cannot be created from a route template (unknown template, missing parameters, failing bean, ...).</summary>
/// <param name="routeId">The id of the route, if known.</param>
/// <param name="templateId">The id of the template.</param>
/// <param name="message">What went wrong.</param>
/// <param name="innerException">The cause, if any.</param>
public class FailedToCreateRouteFromTemplateException(string? routeId, string templateId, string message, Exception? innerException = null)
    : CaravanException($"Failed to create route {routeId ?? "(no id)"} from template {templateId}: {message}", innerException)
{
    /// <summary>The id of the route, if known.</summary>
    public string? RouteId { get; } = routeId;

    /// <summary>The id of the template.</summary>
    public string TemplateId { get; } = templateId;
}
