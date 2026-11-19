namespace Caravan.Spi;

/// <summary>
/// Tracks the lifetime of an exchange from the consumer that created it until routing completes,
/// and runs registered <see cref="ISynchronization"/>s at the end.
/// </summary>
public interface IUnitOfWork
{
    /// <summary>The exchange this unit of work belongs to.</summary>
    Exchange Exchange { get; }

    /// <summary>A snapshot of the message as it was when the exchange was created.</summary>
    Message? OriginalMessage { get; }

    /// <summary>Registers a callback run when the exchange completes.</summary>
    void AddSynchronization(ISynchronization synchronization);

    /// <summary>Whether the callback is registered.</summary>
    bool ContainsSynchronization(ISynchronization synchronization);

    /// <summary>Unregisters a callback.</summary>
    void RemoveSynchronization(ISynchronization synchronization);

    /// <summary>Hands synchronizations over to another exchange (used when an exchange is passed to another thread/route).</summary>
    void HandoverSynchronization(Exchange target, Func<ISynchronization, bool>? filter = null);

    /// <summary>Runs the registered callbacks for <paramref name="exchange"/> (on completion or failure).</summary>
    ValueTask DoneAsync(Exchange exchange);
}
