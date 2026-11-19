using Caravan.Spi;
using Caravan.Support;

namespace Caravan.Processors;

/// <summary>
/// The <c>loop</c> EIP (Camel's <c>LoopProcessor</c>): runs the nested block a number of times computed once by <see cref="Count"/>
/// (<see cref="ExchangePropertyKeys.LoopSize"/>), or while <see cref="While"/> matches (checked before each iteration on the result of the
/// previous one). <see cref="ExchangePropertyKeys.LoopIndex"/> holds the 0-based iteration.
/// </summary>
/// <remarks>
/// <para>
/// Without <see cref="Copy"/> each iteration processes the output of the previous one; with it each iteration starts from a copy of the
/// exchange as it entered the loop, and the exchange ends up with the result of the last iteration. The loop ends early when the exchange
/// fails or is stopped.
/// </para>
/// <para>
/// As in Camel there is no limit on the iterations of a <c>loopWhile</c>. When the exchange's <see cref="Exchange.CancellationToken"/> is
/// cancelled, or the context is stopping, the loop ends quietly with <see cref="BreakOnShutdown"/>; otherwise a cancelled token fails the
/// exchange with an <see cref="OperationCanceledException"/> (so a loop never outlives a forced shutdown).
/// </para>
/// </remarks>
public sealed class LoopProcessor : INavigableProcessor
{
    /// <summary>Creates the processor.</summary>
    /// <param name="block">The nested block.</param>
    /// <param name="count">Computes the number of iterations (converted to <see cref="int"/>); null for a while loop.</param>
    /// <param name="while">The condition of a while loop; null for a counted loop.</param>
    /// <param name="copy">Whether each iteration starts from a copy of the exchange as it entered the loop.</param>
    /// <param name="breakOnShutdown">Whether the loop ends quietly when the exchange is cancelled or the context stops.</param>
    public LoopProcessor(IProcessor block, IExpression? count, IPredicate? @while, bool copy = false, bool breakOnShutdown = false)
    {
        if ((count is null) == (@while is null))
        {
            throw new ArgumentException("A loop needs either a count or a while predicate");
        }

        Block = block;
        Count = count;
        While = @while;
        Copy = copy;
        BreakOnShutdown = breakOnShutdown;
    }

    /// <summary>The nested block.</summary>
    public IProcessor Block { get; }

    /// <summary>Computes the number of iterations; null for a while loop.</summary>
    public IExpression? Count { get; }

    /// <summary>The condition of a while loop; null for a counted loop.</summary>
    public IPredicate? While { get; }

    /// <summary>Whether each iteration starts from a copy of the exchange as it entered the loop.</summary>
    public bool Copy { get; }

    /// <summary>Whether the loop ends quietly when the exchange is cancelled or the context stops.</summary>
    public bool BreakOnShutdown { get; }

    /// <inheritdoc />
    public IReadOnlyList<IProcessor> Next => [Block];

    /// <inheritdoc />
    public async ValueTask ProcessAsync(Exchange exchange)
    {
        var count = 0;
        if (Count is not null)
        {
            var value = Count.Evaluate(exchange);
            count = value is null ? 0 : (int)exchange.Context.TypeConverter.Convert(typeof(int), exchange, value)!;
            exchange.SetProperty(ExchangePropertyKeys.LoopSize, count);
        }

        var current = exchange;
        for (var index = 0; ; index++)
        {
            if (!Pipeline.ContinueProcessing(current) ||
                (While is not null && !While.Matches(current)) ||
                (Count is not null && index >= count) ||
                ShouldBreak(exchange))
            {
                break;
            }

            if (Copy)
            {
                current = exchange.Copy();
                current.UnitOfWork = exchange.UnitOfWork;
            }

            current.SetProperty(ExchangePropertyKeys.LoopIndex, index);
            try
            {
                await Block.ProcessAsync(current).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                current.Exception = e;
            }
        }

        ExchangeHelper.CopyResults(exchange, current);
    }

    /// <inheritdoc />
    public override string ToString() => Count is not null ? $"Loop[{Count}]" : $"LoopWhile[{While}]";

    private bool ShouldBreak(Exchange exchange)
    {
        if (BreakOnShutdown)
        {
            return exchange.CancellationToken.IsCancellationRequested ||
                   exchange.Context.Status is ServiceStatus.Stopping or ServiceStatus.Stopped;
        }

        exchange.CancellationToken.ThrowIfCancellationRequested();
        return false;
    }
}
