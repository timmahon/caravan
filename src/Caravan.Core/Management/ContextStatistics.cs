using Caravan.Spi;

namespace Caravan.Management;

/// <summary>
/// Management view of the context (Camel's <c>ManagedCamelContextMBean</c>). Its counters count each exchange once, when a route receives it
/// from a consumer or template (a <c>direct</c> call to another route does not count again).
/// </summary>
public sealed record ContextStatistics
{
    /// <summary>The context name.</summary>
    public required string Name { get; init; }

    /// <summary>The context status.</summary>
    public ServiceStatus Status { get; init; }

    /// <summary>How long the context has been started (zero when not started).</summary>
    public TimeSpan Uptime { get; init; }

    /// <summary>The number of routes.</summary>
    public int Routes { get; init; }

    /// <summary>The number of started routes.</summary>
    public int StartedRoutes { get; init; }

    /// <summary>The context's counters.</summary>
    public required ProcessingStatistics Statistics { get; init; }
}
