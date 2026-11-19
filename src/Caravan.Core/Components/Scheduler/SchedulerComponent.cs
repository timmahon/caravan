using Caravan.Support;

namespace Caravan.Components.Scheduler;

/// <summary>
/// The <c>scheduler</c> component: fires an empty exchange on every poll of a <see cref="ScheduledPollConsumer"/>,
/// with all its options (<c>initialDelay</c>, <c>delay</c>, <c>useFixedDelay</c>, <c>greedy</c>, backoff, <c>repeatCount</c>, ...).
/// A route can set the exchange property <see cref="ExchangePropertyKeys.SchedulerPolledMessages"/> to false to report
/// an idle poll (driving <c>backoffIdleThreshold</c> and <c>greedy</c>).
/// <para>URI: <c>scheduler:name[?options]</c></para>
/// </summary>
public sealed class SchedulerComponent : ComponentBase
{
    /// <inheritdoc />
    protected override EndpointBase CreateEndpoint(string uri, string remaining, IDictionary<string, string> parameters)
    {
        if (string.IsNullOrEmpty(remaining))
        {
            throw new ArgumentException("scheduler endpoint requires a name, e.g. scheduler:poll");
        }

        return new SchedulerEndpoint(uri, this, remaining);
    }
}
