using Caravan.Spi;

namespace Caravan.Management;

/// <summary>Management view of a route (Camel's <c>ManagedRouteMBean</c>).</summary>
public sealed record RouteStatistics
{
    /// <summary>The route id.</summary>
    public required string RouteId { get; init; }

    /// <summary>The route group, if any.</summary>
    public string? Group { get; init; }

    /// <summary>The route description, if any.</summary>
    public string? Description { get; init; }

    /// <summary>The endpoint the route consumes from, without secrets.</summary>
    public required string EndpointUri { get; init; }

    /// <summary>The route status.</summary>
    public ServiceStatus Status { get; init; }

    /// <summary>How long the route has been started (zero when not started).</summary>
    public TimeSpan Uptime { get; init; }

    /// <summary>The id of the template the route was created from, if any.</summary>
    public string? TemplateId { get; init; }

    /// <summary>The route's counters.</summary>
    public required ProcessingStatistics Statistics { get; init; }
}
