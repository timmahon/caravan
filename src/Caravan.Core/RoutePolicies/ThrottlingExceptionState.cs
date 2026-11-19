namespace Caravan.RoutePolicies;

/// <summary>The circuit state of a <see cref="ThrottlingExceptionRoutePolicy"/>.</summary>
public enum ThrottlingExceptionState
{
    /// <summary>The consumer runs; failures are counted.</summary>
    Closed,

    /// <summary>The consumer is suspended until the half-open delay elapsed.</summary>
    Open,

    /// <summary>The consumer runs again; the next exchange closes the circuit or opens it again.</summary>
    HalfOpen,
}
