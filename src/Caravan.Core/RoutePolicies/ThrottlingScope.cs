namespace Caravan.RoutePolicies;

/// <summary>What <see cref="ThrottlingInflightRoutePolicy"/> counts.</summary>
public enum ThrottlingScope
{
    /// <summary>The exchanges inflight in the route.</summary>
    Route,

    /// <summary>The exchanges inflight in the whole context.</summary>
    Context,
}
