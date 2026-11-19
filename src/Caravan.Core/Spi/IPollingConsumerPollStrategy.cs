namespace Caravan.Spi;

/// <summary>
/// Hooks around each poll of a <see cref="Support.ScheduledPollConsumer"/> (Camel's <c>PollingConsumerPollStrategy</c>),
/// e.g. to open and commit a transaction or to decide whether to retry a failed poll.
/// </summary>
public interface IPollingConsumerPollStrategy
{
    /// <summary>Called before a poll; return false to skip it.</summary>
    ValueTask<bool> BeginAsync(IConsumer consumer, IEndpoint endpoint);

    /// <summary>Called after a successful poll with the number of messages processed.</summary>
    ValueTask CommitAsync(IConsumer consumer, IEndpoint endpoint, int polledMessages);

    /// <summary>Called when the poll failed; return true to retry immediately.</summary>
    /// <param name="consumer">The consumer.</param>
    /// <param name="endpoint">The endpoint polled.</param>
    /// <param name="retryCounter">The number of retries so far for this poll (0 on the first failure).</param>
    /// <param name="cause">The failure.</param>
    ValueTask<bool> RollbackAsync(IConsumer consumer, IEndpoint endpoint, int retryCounter, Exception cause);
}
