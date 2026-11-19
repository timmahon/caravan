namespace Caravan.Spi;

/// <summary>
/// An <see cref="IAggregationRepository"/> that supports optimistic locking (Camel's <c>OptimisticLockingAggregationRepository</c>):
/// with the <c>aggregate</c> EIP's <c>optimisticLocking</c> option, exchanges are aggregated without a lock and the repository
/// detects concurrent updates of a group, throwing an <see cref="OptimisticLockingException"/>; the aggregator then retries with
/// fresh data (see <see cref="Aggregation.OptimisticLockRetryPolicy"/>).
/// </summary>
/// <remarks>
/// <see cref="IAggregationRepository.RemoveAsync"/> must also throw an <see cref="OptimisticLockingException"/> when the stored group
/// is no longer the exchange given (it was changed since it was read). How a repository recognizes "the same" group is up to it: a
/// version kept in the <see cref="ExchangePropertyKeys.AggregationRepositoryVersion"/> property is the usual way for stores that
/// return copies. Versions must never be reused for a key, even after its group was removed: otherwise a group completed and
/// started again could be mistaken for the one read before (the ABA problem), and an update based on stale data would succeed.
/// </remarks>
public interface IOptimisticLockingAggregationRepository : IAggregationRepository
{
    /// <summary>
    /// Stores <paramref name="newExchange"/> as the group of <paramref name="key"/>, provided the stored group is still
    /// <paramref name="oldExchange"/> (as read with <see cref="IAggregationRepository.GetAsync"/>), or there is no group when it is null.
    /// </summary>
    /// <param name="context">The context.</param>
    /// <param name="key">The correlation key.</param>
    /// <param name="oldExchange">The group as read before aggregating, or null for a new group.</param>
    /// <param name="newExchange">The aggregated exchange to store.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <exception cref="OptimisticLockingException">The group was changed (or created) concurrently.</exception>
    ValueTask AddAsync(ICaravanContext context, string key, Exchange? oldExchange, Exchange newExchange, CancellationToken cancellationToken = default);
}
