namespace Caravan.Management;

/// <summary>Management view of one node of a route (Camel's <c>ManagedProcessorMBean</c>).</summary>
public sealed record ProcessorStatistics
{
    /// <summary>The route id.</summary>
    public required string RouteId { get; init; }

    /// <summary>The node id.</summary>
    public required string NodeId { get; init; }

    /// <summary>The EIP name (<c>to</c>, <c>split</c>, ...).</summary>
    public required string ShortName { get; init; }

    /// <summary>The node label, such as <c>to[mock:a]</c>.</summary>
    public required string Label { get; init; }

    /// <summary>The nesting level of the node (1 = a step of the route itself).</summary>
    public int Level { get; init; }

    /// <summary>The node's counters.</summary>
    public required ProcessingStatistics Statistics { get; init; }
}
