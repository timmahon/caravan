using Caravan.Aggregation;
using Caravan.Model;
using Caravan.Spi;
using Caravan.Support;

namespace Caravan.Processors;

/// <summary>
/// The <c>claimCheck</c> EIP (Camel's <c>ClaimCheckProcessor</c>): stores a copy of the exchange in the exchange's
/// <see cref="IClaimCheckRepository"/> (<see cref="ClaimCheckOperation.Set"/> by key, <see cref="ClaimCheckOperation.Push"/> on a stack), or
/// retrieves stored data (<see cref="ClaimCheckOperation.Get"/>, <see cref="ClaimCheckOperation.GetAndRemove"/>, <see cref="ClaimCheckOperation.Pop"/>)
/// and merges it back with <see cref="AggregationStrategy"/> (by default a <see cref="ClaimCheckAggregationStrategy"/> with the filter). Retrieving
/// a key that is not stored, or popping an empty stack, leaves the exchange unchanged.
/// </summary>
/// <remarks>
/// The repository is created on first use and kept in the property <see cref="ExchangePropertyKeys.ClaimCheckRepository"/>; exchange copies share it.
/// </remarks>
public sealed class ClaimCheckProcessor : IProcessor
{
    /// <summary>Creates the processor.</summary>
    /// <param name="operation">The operation.</param>
    /// <param name="key">Computes the key (required for <c>Set</c>, <c>Get</c> and <c>GetAndRemove</c>).</param>
    /// <param name="filter">What <c>Get</c>, <c>GetAndRemove</c> and <c>Pop</c> merge back (see <see cref="ClaimCheckAggregationStrategy"/>).</param>
    /// <param name="aggregationStrategy">Merges retrieved data back instead of the filter-based default.</param>
    /// <exception cref="ArgumentException">The key is missing for an operation that needs it, or the filter is invalid.</exception>
    public ClaimCheckProcessor(ClaimCheckOperation operation, IExpression? key = null, string? filter = null, IAggregationStrategy? aggregationStrategy = null)
    {
        if (key is null && operation is ClaimCheckOperation.Set or ClaimCheckOperation.Get or ClaimCheckOperation.GetAndRemove)
        {
            throw new ArgumentException($"claimCheck: a key is required for the {operation} operation", nameof(key));
        }

        Operation = operation;
        Key = key;
        Filter = filter;
        AggregationStrategy = aggregationStrategy ?? new ClaimCheckAggregationStrategy(filter);
    }

    /// <summary>The operation.</summary>
    public ClaimCheckOperation Operation { get; }

    /// <summary>Computes the key; null for <c>Push</c> and <c>Pop</c>.</summary>
    public IExpression? Key { get; }

    /// <summary>What retrieved data merges back; null for everything.</summary>
    public string? Filter { get; }

    /// <summary>Merges retrieved data back into the exchange.</summary>
    public IAggregationStrategy AggregationStrategy { get; }

    /// <inheritdoc />
    public ValueTask ProcessAsync(Exchange exchange)
    {
        var repository = GetRepository(exchange);
        var key = Key is null ? null : ObjectHelper.ToText(exchange, Key.Evaluate(exchange))
            ?? throw new CaravanExchangeException("claimCheck: the key expression returned null", exchange);
        switch (Operation)
        {
            case ClaimCheckOperation.Set:
                repository.Add(key!, StoredCopy(exchange));
                break;
            case ClaimCheckOperation.Push:
                repository.Push(StoredCopy(exchange));
                break;
            case ClaimCheckOperation.Get:
                MergeBack(exchange, repository.Get(key!));
                break;
            case ClaimCheckOperation.GetAndRemove:
                MergeBack(exchange, repository.GetAndRemove(key!));
                break;
            case ClaimCheckOperation.Pop:
                MergeBack(exchange, repository.Pop());
                break;
        }

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public override string ToString() => Key is null ? $"ClaimCheck[{Operation}]" : $"ClaimCheck[{Operation} {Key}]";

    private static IClaimCheckRepository GetRepository(Exchange exchange)
    {
        if (exchange.GetProperty(ExchangePropertyKeys.ClaimCheckRepository) is IClaimCheckRepository repository)
        {
            return repository;
        }

        repository = new MemoryClaimCheckRepository();
        exchange.SetProperty(ExchangePropertyKeys.ClaimCheckRepository, repository);
        return repository;
    }

    private static Exchange StoredCopy(Exchange exchange)
    {
        var copy = exchange.Copy();
        copy.UnitOfWork = null;
        return copy;
    }

    private void MergeBack(Exchange exchange, Exchange? stored)
    {
        if (stored is null)
        {
            return;
        }

        // Merge a copy, so a stored exchange retrieved several times (Get) is never shared with the route.
        var result = AggregationStrategy.Aggregate(exchange, stored.Copy());
        if (result is not null)
        {
            ExchangeHelper.CopyResults(exchange, result);
        }
    }
}
