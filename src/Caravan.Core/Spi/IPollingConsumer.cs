namespace Caravan.Spi;

/// <summary>
/// A consumer the caller pulls messages from (Camel's <c>PollingConsumer</c>), used by <see cref="IConsumerTemplate"/>
/// and EIPs such as <c>pollEnrich</c>. A received exchange may carry completion callbacks: run them when done
/// (<see cref="IConsumerTemplate.DoneUoWAsync"/>).
/// </summary>
public interface IPollingConsumer : IService
{
    /// <summary>The endpoint polled.</summary>
    IEndpoint Endpoint { get; }

    /// <summary>Waits until a message is available and returns it.</summary>
    /// <exception cref="OperationCanceledException">The token was cancelled or the consumer stopped.</exception>
    Task<Exchange> ReceiveAsync(CancellationToken cancellationToken = default);

    /// <summary>Waits up to <paramref name="timeout"/> for a message; returns null when none arrived in time.</summary>
    Task<Exchange?> ReceiveAsync(TimeSpan timeout, CancellationToken cancellationToken = default);

    /// <summary>Returns a message if one is immediately available, otherwise null.</summary>
    Exchange? ReceiveNoWait();
}
