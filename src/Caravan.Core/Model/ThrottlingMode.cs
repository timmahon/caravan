namespace Caravan.Model;

/// <summary>How the <c>throttle</c> EIP limits exchanges (Camel's <c>ThrottlingMode</c>).</summary>
public enum ThrottlingMode
{
    /// <summary>At most N exchanges in flight at a time: a permit is taken when the exchange passes the throttle and returned when it completes (Camel 4's throttler).</summary>
    ConcurrentRequests,

    /// <summary>At most N exchanges per time period, over a sliding window: a permit taken is returned one period later (Camel 3's throttler).</summary>
    TotalRequests,
}
