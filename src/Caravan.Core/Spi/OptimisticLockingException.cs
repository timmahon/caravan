namespace Caravan.Spi;

/// <summary>
/// Thrown by an <see cref="IOptimisticLockingAggregationRepository"/> when a group was changed concurrently since it was read; the
/// aggregator retries the operation.
/// </summary>
public class OptimisticLockingException : CaravanException
{
    /// <summary>Creates the exception.</summary>
    public OptimisticLockingException()
        : base("The aggregation group was changed concurrently")
    {
    }

    /// <summary>Creates the exception with a message.</summary>
    /// <param name="message">The message.</param>
    public OptimisticLockingException(string message)
        : base(message)
    {
    }
}
