using Caravan.Spi;

namespace Caravan.Processors;

/// <summary>
/// The <c>rollback</c> EIP: marks the exchange rollback-only (or only the current route with <paramref name="markRollbackOnlyLast"/>)
/// and, unless it should only mark it, fails it with a <see cref="RollbackExchangeException"/>.
/// </summary>
public sealed class RollbackProcessor(string? message, bool markRollbackOnly, bool markRollbackOnlyLast) : IProcessor
{
    /// <inheritdoc />
    /// <exception cref="RollbackExchangeException">Unless only marking the exchange.</exception>
    public ValueTask ProcessAsync(Exchange exchange)
    {
        if (markRollbackOnlyLast)
        {
            exchange.IsRollbackOnlyLast = true;
        }
        else
        {
            exchange.IsRollbackOnly = true;
        }

        if (markRollbackOnly || markRollbackOnlyLast)
        {
            return ValueTask.CompletedTask;
        }

        throw new RollbackExchangeException(message, exchange);
    }

    /// <inheritdoc />
    public override string ToString() => markRollbackOnlyLast ? "MarkRollbackOnlyLast" : markRollbackOnly ? "MarkRollbackOnly" : "Rollback";
}
