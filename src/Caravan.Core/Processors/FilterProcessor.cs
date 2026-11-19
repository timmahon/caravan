using Caravan.Spi;

namespace Caravan.Processors;

/// <summary>
/// The <c>filter</c> EIP: runs the nested block only when the predicate matches and records the outcome in
/// <see cref="ExchangePropertyKeys.FilterMatched"/>. Also used for each <c>when</c> of a <c>choice</c>.
/// </summary>
public sealed class FilterProcessor(IPredicate predicate, IProcessor block) : INavigableProcessor
{
    /// <summary>The condition.</summary>
    public IPredicate Predicate { get; } = predicate;

    /// <summary>The nested block run when the predicate matches.</summary>
    public IProcessor Block { get; } = block;

    /// <inheritdoc />
    public IReadOnlyList<IProcessor> Next => [Block];

    /// <summary>Evaluates the predicate and sets <see cref="ExchangePropertyKeys.FilterMatched"/> to the result.</summary>
    public bool Matches(Exchange exchange)
    {
        var matches = Predicate.Matches(exchange);
        exchange.SetProperty(ExchangePropertyKeys.FilterMatched, matches);
        return matches;
    }

    /// <inheritdoc />
    public ValueTask ProcessAsync(Exchange exchange) => Matches(exchange) ? Block.ProcessAsync(exchange) : ValueTask.CompletedTask;

    /// <inheritdoc />
    public override string ToString() => $"Filter[{Predicate}]";
}
