namespace Caravan.Spi;

/// <summary>A callback invoked when an exchange completes (Camel's <c>Synchronization</c>).</summary>
public interface ISynchronization
{
    ValueTask OnCompleteAsync(Exchange exchange);

    ValueTask OnFailureAsync(Exchange exchange);
}

/// <summary>
/// Tracks the lifetime of an exchange from the consumer that created it until routing completes,
/// and runs registered <see cref="ISynchronization"/>s at the end.
/// </summary>
public interface IUnitOfWork
{
    Exchange Exchange { get; }

    /// <summary>A snapshot of the message as it was when the exchange was created.</summary>
    Message? OriginalMessage { get; }

    void AddSynchronization(ISynchronization synchronization);

    bool ContainsSynchronization(ISynchronization synchronization);

    void RemoveSynchronization(ISynchronization synchronization);

    /// <summary>Hands synchronizations over to another exchange (used when an exchange is passed to another thread/route).</summary>
    void HandoverSynchronization(Exchange target, Func<ISynchronization, bool>? filter = null);

    ValueTask DoneAsync(Exchange exchange);
}
