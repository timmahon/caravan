namespace Caravan.Spi;

/// <summary>
/// A route policy (Camel's <c>RoutePolicy</c>): callbacks on a route's lifecycle and on every exchange it processes, typically used to throttle
/// or suspend the route's consumer. Policies come from <see cref="Model.RouteDefinition.RoutePolicies"/>,
/// <see cref="Model.RouteDefinition.RoutePolicyRefs"/> and the context's <see cref="ICaravanContext.RoutePolicyFactories"/>. An exception thrown by
/// a callback is logged and ignored. Every method has an empty default.
/// </summary>
public interface IRoutePolicy
{
    /// <summary>Called when the route is created (before it starts).</summary>
    /// <param name="route">The route.</param>
    void OnInit(IRoute route)
    {
    }

    /// <summary>Called when the route is removed from the context.</summary>
    /// <param name="route">The route.</param>
    void OnRemove(IRoute route)
    {
    }

    /// <summary>Called when the route has started.</summary>
    /// <param name="route">The route.</param>
    void OnStart(IRoute route)
    {
    }

    /// <summary>Called when the route has stopped.</summary>
    /// <param name="route">The route.</param>
    void OnStop(IRoute route)
    {
    }

    /// <summary>Called when the route has been suspended.</summary>
    /// <param name="route">The route.</param>
    void OnSuspend(IRoute route)
    {
    }

    /// <summary>Called when the route has been resumed.</summary>
    /// <param name="route">The route.</param>
    void OnResume(IRoute route)
    {
    }

    /// <summary>Called when an exchange enters the route, before its first step.</summary>
    /// <param name="route">The route.</param>
    /// <param name="exchange">The exchange.</param>
    void OnExchangeBegin(IRoute route, Exchange exchange)
    {
    }

    /// <summary>Called when an exchange leaves the route (after its unit of work completed, for exchanges the route created).</summary>
    /// <param name="route">The route.</param>
    /// <param name="exchange">The exchange.</param>
    void OnExchangeDone(IRoute route, Exchange exchange)
    {
    }
}
