using Caravan.Spi;

namespace Caravan.Support;

/// <summary>
/// Base class for endpoints whose consumer is a <see cref="ScheduledPollConsumer"/>: exposes the scheduling options as
/// URI options (<c>initialDelay</c>, <c>delay</c>, <c>useFixedDelay</c>, <c>greedy</c>, <c>sendEmptyMessageWhenIdle</c>,
/// <c>backoffMultiplier</c>, <c>backoffIdleThreshold</c>, <c>backoffErrorThreshold</c>, <c>startScheduler</c>,
/// <c>repeatCount</c>, <c>pollStrategy</c>). Call <see cref="ConfigureConsumer"/> on the consumers it creates.
/// </summary>
public abstract class ScheduledPollEndpoint : EndpointBase
{
    /// <summary>Creates an endpoint.</summary>
    protected ScheduledPollEndpoint(string endpointUri, IComponent? component, ICaravanContext context)
        : base(endpointUri, component, context)
    {
    }

    /// <inheritdoc cref="ScheduledPollConsumer.InitialDelay" />
    public TimeSpan InitialDelay { get; set; } = TimeSpan.FromSeconds(1);

    /// <inheritdoc cref="ScheduledPollConsumer.Delay" />
    public TimeSpan Delay { get; set; } = TimeSpan.FromMilliseconds(500);

    /// <inheritdoc cref="ScheduledPollConsumer.UseFixedDelay" />
    public bool UseFixedDelay { get; set; } = true;

    /// <inheritdoc cref="ScheduledPollConsumer.Greedy" />
    public bool Greedy { get; set; }

    /// <inheritdoc cref="ScheduledPollConsumer.SendEmptyMessageWhenIdle" />
    public bool SendEmptyMessageWhenIdle { get; set; }

    /// <inheritdoc cref="ScheduledPollConsumer.BackoffMultiplier" />
    public int BackoffMultiplier { get; set; }

    /// <inheritdoc cref="ScheduledPollConsumer.BackoffIdleThreshold" />
    public int BackoffIdleThreshold { get; set; }

    /// <inheritdoc cref="ScheduledPollConsumer.BackoffErrorThreshold" />
    public int BackoffErrorThreshold { get; set; }

    /// <inheritdoc cref="ScheduledPollConsumer.StartScheduler" />
    public bool StartScheduler { get; set; } = true;

    /// <inheritdoc cref="ScheduledPollConsumer.RepeatCount" />
    public long RepeatCount { get; set; }

    /// <summary>Hooks around each poll (e.g. <c>#myStrategy</c> from the registry); null for the default.</summary>
    public IPollingConsumerPollStrategy? PollStrategy { get; set; }

    /// <summary>Copies the scheduling options onto <paramref name="consumer"/>.</summary>
    protected void ConfigureConsumer(ScheduledPollConsumer consumer)
    {
        consumer.InitialDelay = InitialDelay;
        consumer.Delay = Delay;
        consumer.UseFixedDelay = UseFixedDelay;
        consumer.Greedy = Greedy;
        consumer.SendEmptyMessageWhenIdle = SendEmptyMessageWhenIdle;
        consumer.BackoffMultiplier = BackoffMultiplier;
        consumer.BackoffIdleThreshold = BackoffIdleThreshold;
        consumer.BackoffErrorThreshold = BackoffErrorThreshold;
        consumer.StartScheduler = StartScheduler;
        consumer.RepeatCount = RepeatCount;
        if (PollStrategy is not null)
        {
            consumer.PollStrategy = PollStrategy;
        }
    }
}
