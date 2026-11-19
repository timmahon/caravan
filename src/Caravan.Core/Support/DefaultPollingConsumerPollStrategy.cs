using Caravan.Spi;

namespace Caravan.Support;

/// <summary>The default poll strategy: always polls, never retries a failed poll (it is tried again at the next scheduled poll).</summary>
public class DefaultPollingConsumerPollStrategy : IPollingConsumerPollStrategy
{
    /// <inheritdoc />
    public virtual ValueTask<bool> BeginAsync(IConsumer consumer, IEndpoint endpoint) => ValueTask.FromResult(true);

    /// <inheritdoc />
    public virtual ValueTask CommitAsync(IConsumer consumer, IEndpoint endpoint, int polledMessages) => ValueTask.CompletedTask;

    /// <inheritdoc />
    public virtual ValueTask<bool> RollbackAsync(IConsumer consumer, IEndpoint endpoint, int retryCounter, Exception cause) =>
        ValueTask.FromResult(false);
}
