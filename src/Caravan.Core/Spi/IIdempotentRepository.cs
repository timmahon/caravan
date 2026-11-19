namespace Caravan.Spi;

/// <summary>
/// Remembers the ids of processed messages for the <c>idempotentConsumer</c> EIP (Camel's <c>IdempotentRepository</c>).
/// <see cref="Idempotent.MemoryIdempotentRepository"/> is the in-memory implementation.
/// </summary>
/// <remarks>
/// <para>
/// All operations are asynchronous so durable stores (SQL, Redis, files) fit; implementations must be thread-safe, and
/// <see cref="AddAsync"/> must be atomic (only one of concurrent callers adding the same key gets true): the eager idempotent consumer relies
/// on it to let exactly one of concurrent duplicates through. Repositories that are <see cref="IService"/>s are started and stopped with the
/// processor using them.
/// </para>
/// <para>
/// Lifecycle of a key with the default (eager) consumer: <see cref="AddAsync"/> when the exchange arrives, then <see cref="ConfirmAsync"/> when it
/// completed successfully, or <see cref="RemoveAsync"/> when it failed (with <c>removeOnFailure</c>), so it can be retried. A store that
/// distinguishes in-progress from confirmed keys can expire unconfirmed ones; the in-memory repository treats added keys as confirmed.
/// </para>
/// </remarks>
public interface IIdempotentRepository
{
    /// <summary>Adds <paramref name="key"/>; returns true if it was added, false if it was already present (a duplicate).</summary>
    /// <param name="key">The message id.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    ValueTask<bool> AddAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>Whether <paramref name="key"/> is present.</summary>
    /// <param name="key">The message id.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    ValueTask<bool> ContainsAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>Removes <paramref name="key"/>; returns whether it was present.</summary>
    /// <param name="key">The message id.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    ValueTask<bool> RemoveAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>Confirms <paramref name="key"/>: the message was processed successfully; returns whether it was confirmed.</summary>
    /// <param name="key">The message id.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    ValueTask<bool> ConfirmAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>Removes all keys.</summary>
    /// <param name="cancellationToken">Cancels the operation.</param>
    ValueTask ClearAsync(CancellationToken cancellationToken = default);
}
