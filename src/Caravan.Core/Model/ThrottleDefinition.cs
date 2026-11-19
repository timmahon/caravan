using Caravan.Processors;
using Caravan.Spi;

namespace Caravan.Model;

/// <summary>
/// The <c>throttle</c> EIP: limits the exchanges passing through, concurrently or per time period; see <see cref="ThrottleProcessor"/>.
/// </summary>
public sealed class ThrottleDefinition(IExpression maxRequests) : ProcessorDefinition
{
    /// <summary>Computes the maximum number of requests (concurrent, or per period), for each exchange.</summary>
    public IExpression MaxRequests { get; set; } = maxRequests;

    /// <summary>
    /// The throttling mode; when null, <see cref="ThrottlingMode.TotalRequests"/> if <see cref="TimePeriod"/> is set, else
    /// <see cref="ThrottlingMode.ConcurrentRequests"/> (Camel 4's default).
    /// </summary>
    public ThrottlingMode? Mode { get; set; }

    /// <summary>The period of <see cref="ThrottlingMode.TotalRequests"/>; null for the default (1 second).</summary>
    public TimeSpan? TimePeriod { get; set; }

    /// <summary>Computes the key that exchanges are throttled by (each key has its own limit); null for one shared limit.</summary>
    public IExpression? CorrelationExpression { get; set; }

    /// <summary>Whether exchanges over the limit fail with a <see cref="ThrottlerRejectedExecutionException"/> instead of waiting.</summary>
    public bool RejectExecution { get; set; }

    /// <summary>Whether waiting does not block the calling thread (default true).</summary>
    public bool AsyncDelayed { get; set; } = true;

    /// <summary>Accepted for parity with Camel; has no effect.</summary>
    public bool CallerRunsWhenRejected { get; set; } = true;

    /// <summary>The mode in effect (see <see cref="Mode"/>).</summary>
    public ThrottlingMode EffectiveMode => Mode ?? (TimePeriod is null ? ThrottlingMode.ConcurrentRequests : ThrottlingMode.TotalRequests);

    /// <inheritdoc />
    public override string ShortName => "throttle";

    /// <inheritdoc />
    public override string Label => $"throttle[{MaxRequests}]";

    /// <inheritdoc />
    public override IProcessor CreateProcessor(RouteContext routeContext) =>
        new ThrottleProcessor(MaxRequests, EffectiveMode, TimePeriod, CorrelationExpression, RejectExecution, AsyncDelayed, CallerRunsWhenRejected);
}
