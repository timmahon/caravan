namespace Caravan.Spi;

/// <summary>
/// Stores the groups of the <c>aggregate</c> EIP while they are being aggregated (Camel's <c>AggregationRepository</c>): one
/// exchange per correlation key, the result so far.
/// </summary>
/// <remarks>
/// <para>
/// Implementations must store <b>copies</b> (or serialized forms) of the exchanges they are given and return copies from
/// <see cref="GetAsync"/>, as a durable store would, so that an exchange handed to the repository can be changed afterwards without
/// affecting what is stored. The exchange id must be kept: <see cref="ConfirmAsync"/> refers to completed groups by exchange id.
/// </para>
/// <para>
/// Unless optimistic locking is used (<see cref="IOptimisticLockingAggregationRepository"/>), the aggregator serializes the calls
/// for one processor, so implementations only need to be safe for use by several aggregators (or processes) at once.
/// Implementations that are services (<see cref="IService"/>) are started and stopped with the aggregator.
/// </para>
/// </remarks>
public interface IAggregationRepository
{
    /// <summary>Stores <paramref name="exchange"/> as the group of <paramref name="key"/>, replacing any previous one.</summary>
    /// <param name="context">The context.</param>
    /// <param name="key">The correlation key.</param>
    /// <param name="exchange">The aggregated exchange so far.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    ValueTask AddAsync(ICaravanContext context, string key, Exchange exchange, CancellationToken cancellationToken = default);

    /// <summary>Gets (a copy of) the group of <paramref name="key"/>, or null when there is none.</summary>
    /// <param name="context">The context.</param>
    /// <param name="key">The correlation key.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    ValueTask<Exchange?> GetAsync(ICaravanContext context, string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes the group of <paramref name="key"/> because it completed. A recoverable repository keeps the exchange until it is
    /// confirmed (<see cref="ConfirmAsync"/>).
    /// </summary>
    /// <param name="context">The context.</param>
    /// <param name="key">The correlation key.</param>
    /// <param name="exchange">
    /// The completed (aggregated) exchange, which a recoverable repository keeps under its exchange id. It carries the properties of the
    /// exchange read for the group that identify its version (e.g. <see cref="ExchangePropertyKeys.AggregationRepositoryVersion"/>).
    /// </param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    ValueTask RemoveAsync(ICaravanContext context, string key, Exchange exchange, CancellationToken cancellationToken = default);

    /// <summary>Confirms that the completed exchange <paramref name="exchangeId"/> was processed (or discarded) and can be forgotten.</summary>
    /// <param name="context">The context.</param>
    /// <param name="exchangeId">The id of the completed exchange.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    ValueTask ConfirmAsync(ICaravanContext context, string exchangeId, CancellationToken cancellationToken = default);

    /// <summary>The correlation keys of the groups currently stored.</summary>
    /// <param name="cancellationToken">Cancels the operation.</param>
    ValueTask<IReadOnlyCollection<string>> GetKeysAsync(CancellationToken cancellationToken = default);
}
