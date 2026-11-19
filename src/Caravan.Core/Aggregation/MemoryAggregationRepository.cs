using Caravan.Spi;
using Caravan.Support;

namespace Caravan.Aggregation;

/// <summary>
/// The default <see cref="IAggregationRepository"/> of the <c>aggregate</c> EIP: groups are kept in memory (Camel's
/// <c>MemoryAggregationRepository</c>) and lost when the repository stops. Exchanges are stored and returned as copies, as a durable
/// store would.
/// </summary>
/// <remarks>
/// With optimistic locking each group carries a version: <see cref="GetAsync"/> sets it in the
/// <see cref="ExchangePropertyKeys.AggregationRepositoryVersion"/> property of the copy it returns, and the conditional
/// <see cref="AddAsync(ICaravanContext, string, Exchange?, Exchange, CancellationToken)"/> and <see cref="RemoveAsync"/> fail with an
/// <see cref="OptimisticLockingException"/> when the stored version differs. The list kept in
/// <see cref="ExchangePropertyKeys.GroupedExchange"/> by the grouped strategies is copied too, so a failed attempt leaves the stored
/// group unchanged.
/// </remarks>
public sealed class MemoryAggregationRepository : ServiceSupport, IOptimisticLockingAggregationRepository
{
    private readonly Lock _lock = new();
    private readonly Dictionary<string, Entry> _groups = new(StringComparer.Ordinal);

    // Versions are unique across keys and never reused, so a group removed and created again is never mistaken for the one read
    // before (the ABA problem).
    private long _lastVersion;

    /// <summary>Creates the repository.</summary>
    /// <param name="optimisticLocking">Whether to detect concurrent updates (for the aggregator's <c>optimisticLocking</c> option).</param>
    public MemoryAggregationRepository(bool optimisticLocking = false)
    {
        OptimisticLocking = optimisticLocking;
    }

    /// <summary>Whether concurrent updates are detected.</summary>
    public bool OptimisticLocking { get; }

    /// <summary>The number of groups stored.</summary>
    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _groups.Count;
            }
        }
    }

    /// <inheritdoc />
    public ValueTask AddAsync(ICaravanContext context, string key, Exchange exchange, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(exchange);
        var stored = Store(exchange);
        lock (_lock)
        {
            _groups[key] = new Entry(stored, ++_lastVersion);
        }

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask AddAsync(ICaravanContext context, string key, Exchange? oldExchange, Exchange newExchange, CancellationToken cancellationToken = default)
    {
        if (!OptimisticLocking)
        {
            return AddAsync(context, key, newExchange, cancellationToken);
        }

        ArgumentNullException.ThrowIfNull(newExchange);
        var stored = Store(newExchange);
        lock (_lock)
        {
            var exists = _groups.TryGetValue(key, out var existing);
            if (oldExchange is null ? exists : !exists || existing.Version != VersionOf(oldExchange))
            {
                throw new OptimisticLockingException($"The aggregation group '{key}' was changed concurrently");
            }

            _groups[key] = new Entry(stored, ++_lastVersion);
        }

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask<Exchange?> GetAsync(ICaravanContext context, string key, CancellationToken cancellationToken = default)
    {
        Entry entry;
        lock (_lock)
        {
            if (!_groups.TryGetValue(key, out entry))
            {
                return ValueTask.FromResult<Exchange?>(null);
            }
        }

        var copy = entry.Exchange.Copy();
        if (OptimisticLocking)
        {
            CopyGroupedList(copy);
            copy.SetProperty(ExchangePropertyKeys.AggregationRepositoryVersion, entry.Version);
        }

        return ValueTask.FromResult<Exchange?>(copy);
    }

    /// <inheritdoc />
    public ValueTask RemoveAsync(ICaravanContext context, string key, Exchange exchange, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            if (OptimisticLocking && (!_groups.TryGetValue(key, out var existing) || existing.Version != VersionOf(exchange)))
            {
                throw new OptimisticLockingException($"The aggregation group '{key}' was changed concurrently");
            }

            _groups.Remove(key);
        }

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask ConfirmAsync(ICaravanContext context, string exchangeId, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

    /// <inheritdoc />
    public ValueTask<IReadOnlyCollection<string>> GetKeysAsync(CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            return ValueTask.FromResult<IReadOnlyCollection<string>>([.. _groups.Keys]);
        }
    }

    /// <summary>Clears the groups (as Camel's memory repository does).</summary>
    protected override Task DoStopAsync(CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            _groups.Clear();
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public override string ToString() => OptimisticLocking ? "MemoryAggregationRepository[optimistic]" : "MemoryAggregationRepository";

    private static long VersionOf(Exchange exchange) => exchange.GetProperty(ExchangePropertyKeys.AggregationRepositoryVersion) switch
    {
        long l => l,
        int i => i,
        null => -1,
        var other => Convert.ToInt64(other, System.Globalization.CultureInfo.InvariantCulture),
    };

    private Exchange Store(Exchange exchange)
    {
        var copy = exchange.Copy();
        copy.RemoveProperty(ExchangePropertyKeys.AggregationRepositoryVersion);
        if (OptimisticLocking)
        {
            CopyGroupedList(copy);
        }

        return copy;
    }

    private static void CopyGroupedList(Exchange exchange)
    {
        if (exchange.GetProperty(ExchangePropertyKeys.GroupedExchange) is System.Collections.IList list &&
            list.GetType() is { IsGenericType: true } type &&
            type.GetGenericTypeDefinition() == typeof(List<>))
        {
            exchange.SetProperty(ExchangePropertyKeys.GroupedExchange, Activator.CreateInstance(type, list));
        }
    }

    private readonly record struct Entry(Exchange Exchange, long Version);
}
