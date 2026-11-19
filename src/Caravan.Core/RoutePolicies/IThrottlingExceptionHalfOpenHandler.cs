namespace Caravan.RoutePolicies;

/// <summary>Decides whether an open <see cref="ThrottlingExceptionRoutePolicy"/> may let exchanges through again (Camel's <c>ThrottlingExceptionHalfOpenHandler</c>).</summary>
public interface IThrottlingExceptionHalfOpenHandler
{
    /// <summary>Whether the circuit may be half-opened (e.g. the remote system answers a health probe).</summary>
    /// <returns>True to resume the consumer; false to stay open for another <see cref="ThrottlingExceptionRoutePolicy.HalfOpenAfter"/>.</returns>
    bool IsReadyToBeClosed();
}
