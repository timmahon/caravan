using Caravan.Spi;

namespace Caravan.Processors;

/// <summary>
/// The <c>choice</c> EIP: runs the block of the first <c>when</c> whose predicate matches, or the
/// <c>otherwise</c> block when none does. Routing continues after the choice either way.
/// </summary>
public sealed class ChoiceProcessor(IReadOnlyList<FilterProcessor> whens, IProcessor? otherwise) : INavigableProcessor
{
    /// <summary>The <c>when</c> clauses, in order.</summary>
    public IReadOnlyList<FilterProcessor> Whens { get; } = whens;

    /// <summary>The <c>otherwise</c> block, if any.</summary>
    public IProcessor? Otherwise { get; } = otherwise;

    /// <inheritdoc />
    public IReadOnlyList<IProcessor> Next => Otherwise is null ? [.. Whens] : [.. Whens, Otherwise];

    /// <inheritdoc />
    public ValueTask ProcessAsync(Exchange exchange)
    {
        foreach (var when in Whens)
        {
            if (when.Matches(exchange))
            {
                return when.Block.ProcessAsync(exchange);
            }
        }

        return Otherwise?.ProcessAsync(exchange) ?? ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public override string ToString() => $"Choice[{string.Join(", ", Whens)}{(Otherwise is null ? string.Empty : ", Otherwise")}]";
}
