namespace Caravan.Spi;

/// <summary>
/// An <see cref="IAggregationRepository"/> that keeps completed exchanges until they are confirmed (Camel's
/// <c>RecoverableAggregationRepository</c>), so that aggregated exchanges whose processing failed (or was interrupted by a crash)
/// are sent again. When <see cref="UseRecovery"/> is true the aggregator scans the repository every <see cref="RecoveryInterval"/>
/// and resubmits unconfirmed exchanges it is not processing, with the <see cref="CaravanHeaders.Redelivered"/> and
/// <see cref="CaravanHeaders.RedeliveryCounter"/> headers set; after <see cref="MaximumRedeliveries"/> attempts the exchange is sent
/// to <see cref="DeadLetterUri"/> and confirmed.
/// </summary>
/// <remarks>
/// <see cref="IAggregationRepository.RemoveAsync"/> moves the group to the completed exchanges (keyed by exchange id) and
/// <see cref="IAggregationRepository.ConfirmAsync"/> deletes it from there.
/// </remarks>
public interface IRecoverableAggregationRepository : IAggregationRepository
{
    /// <summary>Whether the aggregator recovers unconfirmed exchanges.</summary>
    bool UseRecovery { get; }

    /// <summary>How often the aggregator scans for exchanges to recover.</summary>
    TimeSpan RecoveryInterval { get; }

    /// <summary>The maximum number of recovery attempts per exchange; 0 or less for no limit.</summary>
    int MaximumRedeliveries { get; }

    /// <summary>Where exchanges go once <see cref="MaximumRedeliveries"/> is exhausted; required when it is set.</summary>
    string? DeadLetterUri { get; }

    /// <summary>The ids of the completed exchanges that were not confirmed yet.</summary>
    /// <param name="context">The context.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    ValueTask<IReadOnlyCollection<string>> ScanAsync(ICaravanContext context, CancellationToken cancellationToken = default);

    /// <summary>Gets (a copy of) the completed exchange <paramref name="exchangeId"/>, or null when it was confirmed.</summary>
    /// <param name="context">The context.</param>
    /// <param name="exchangeId">The id of the completed exchange.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    ValueTask<Exchange?> RecoverAsync(ICaravanContext context, string exchangeId, CancellationToken cancellationToken = default);
}
