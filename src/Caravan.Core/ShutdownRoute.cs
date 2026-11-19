namespace Caravan;

/// <summary>When a route's consumer is stopped during a graceful shutdown (Camel's <c>ShutdownRoute</c>).</summary>
public enum ShutdownRoute
{
    /// <summary>The consumer is stopped first, before waiting for inflight exchanges.</summary>
    Default,

    /// <summary>
    /// The consumer keeps running until the other routes have completed their inflight exchanges, then stops
    /// (for routes fed by other routes, e.g. a <c>direct</c> or <c>seda</c> route used by the rest).
    /// </summary>
    Defer,
}
