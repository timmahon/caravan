namespace Caravan.Management;

/// <summary>Which performance counters are kept (Camel's <c>ManagementStatisticsLevel</c>).</summary>
public enum ManagementStatisticsLevel
{
    /// <summary>Context and route counters only.</summary>
    RoutesOnly,

    /// <summary>Context, route and node counters (default).</summary>
    Default,

    /// <summary>No context or node counters (route counters are always kept: they feed the <c>controlbus</c> stats).</summary>
    Off,
}
