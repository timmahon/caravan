namespace Caravan.Management;

/// <summary>Management view of an endpoint with its usage (Camel's <c>ManagedEndpointMBean</c> and runtime endpoint registry).</summary>
public sealed record EndpointInfo
{
    /// <summary>The endpoint URI, without secrets.</summary>
    public required string Uri { get; init; }

    /// <summary>The component scheme.</summary>
    public required string Scheme { get; init; }

    /// <summary>Whether the endpoint is cached and shared (singleton).</summary>
    public bool Singleton { get; init; }

    /// <summary>The endpoint status.</summary>
    public Spi.ServiceStatus Status { get; init; }

    /// <summary>Exchanges received by routes consuming from the endpoint.</summary>
    public long InboundHits { get; init; }

    /// <summary>Exchanges sent to the endpoint (by routes and templates).</summary>
    public long OutboundHits { get; init; }

    /// <summary>The ids of the routes consuming from the endpoint.</summary>
    public IReadOnlyList<string> ConsumingRoutes { get; init; } = [];
}
