using Caravan.Spi;

namespace Caravan.Processors;

/// <summary>The <c>validate</c> EIP: fails the exchange with a <see cref="PredicateValidationException"/> when <see cref="Predicate"/> does not match.</summary>
public sealed class ValidateProcessor(IPredicate predicate) : IProcessor
{
    /// <summary>The condition the exchange must meet.</summary>
    public IPredicate Predicate { get; } = predicate;

    /// <inheritdoc />
    public ValueTask ProcessAsync(Exchange exchange)
    {
        if (!Predicate.Matches(exchange))
        {
            throw new PredicateValidationException(exchange, Predicate);
        }

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public override string ToString() => $"Validate[{Predicate}]";
}
