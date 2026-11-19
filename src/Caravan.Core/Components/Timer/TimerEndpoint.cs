using Caravan.Spi;
using Caravan.Support;

namespace Caravan.Components.Timer;

/// <summary>A <c>timer</c> endpoint (consumer only).</summary>
public sealed class TimerEndpoint(string uri, TimerComponent component, string timerName)
    : EndpointBase(uri, component, component.Context!)
{
    /// <summary>The timer name (the URI path).</summary>
    public string TimerName { get; } = timerName;

    /// <summary>Interval between fires (default 1s); zero fires once.</summary>
    public TimeSpan Period { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>Delay before the first fire (default 1s).</summary>
    public TimeSpan Delay { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>Maximum number of fires (0 = unlimited).</summary>
    public long RepeatCount { get; set; }

    /// <summary>
    /// False (default): each fire is scheduled <see cref="Period"/> after the previous one started (fixed delay);
    /// true: fires are scheduled at a fixed rate from the first one.
    /// </summary>
    public bool FixedRate { get; set; }

    /// <summary>
    /// Set the timer's name, counter, period and fired time as exchange properties
    /// (<c>CaravanTimerName</c>, <c>CaravanTimerCounter</c>, <c>CaravanTimerPeriod</c>, <c>CaravanTimerFiredTime</c>)
    /// and the <c>firedTime</c> and <c>CaravanMessageTimestamp</c> headers (default false, as in Camel).
    /// </summary>
    public bool IncludeMetadata { get; set; }

    /// <summary>Not supported: the timer only produces exchanges.</summary>
    /// <exception cref="NotSupportedException">Always.</exception>
    public override IProducer CreateProducer() =>
        throw new NotSupportedException("The timer component does not support producers");

    /// <inheritdoc />
    public override IConsumer CreateConsumer(IProcessor processor) => new TimerConsumer(this, processor);
}
