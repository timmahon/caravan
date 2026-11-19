using Caravan.Spi;

namespace Caravan.Processors;

/// <summary>
/// The <c>doTry</c> EIP. Runs the try block; when it fails, the first <c>doCatch</c> clause that catches the exception runs with
/// the exception cleared and stored in <see cref="ExchangePropertyKeys.ExceptionCaught"/> (routing then continues after the
/// <c>doTry</c> unless the clause fails); the <c>doFinally</c> block always runs (unless the exchange was stopped), after which an
/// exception that was not caught is restored and propagates.
/// </summary>
public sealed class TryProcessor(IProcessor tryBlock, IReadOnlyList<CatchProcessor> catches, IProcessor? finallyBlock) : INavigableProcessor
{
    /// <summary>The try block.</summary>
    public IProcessor TryBlock { get; } = tryBlock;

    /// <summary>The <c>doCatch</c> clauses, in order.</summary>
    public IReadOnlyList<CatchProcessor> Catches { get; } = catches;

    /// <summary>The <c>doFinally</c> block, if any.</summary>
    public IProcessor? FinallyBlock { get; } = finallyBlock;

    /// <inheritdoc />
    public IReadOnlyList<IProcessor> Next => FinallyBlock is null ? [TryBlock, .. Catches] : [TryBlock, .. Catches, FinallyBlock];

    /// <inheritdoc />
    public async ValueTask ProcessAsync(Exchange exchange)
    {
        await RunAsync(TryBlock, exchange).ConfigureAwait(false);

        if (exchange.Exception is { } exception && !exchange.IsRouteStop)
        {
            foreach (var clause in Catches)
            {
                if (!clause.Catches(exchange, exception))
                {
                    continue;
                }

                if (exchange.GetProperty(ExchangePropertyKeys.ToEndpoint) is { } endpoint)
                {
                    exchange.SetProperty(ExchangePropertyKeys.FailureEndpoint, endpoint);
                }

                if (exchange.CurrentRouteId is { } routeId)
                {
                    exchange.SetProperty(ExchangePropertyKeys.FailureRouteId, routeId);
                }

                exchange.SetProperty(ExchangePropertyKeys.ExceptionCaught, exception);
                exchange.Exception = null;
                exchange.IsRedeliveryExhausted = false;
                exchange.IsRollbackOnly = false;
                exchange.IsRollbackOnlyLast = false;
                await RunAsync(clause, exchange).ConfigureAwait(false);
                break;
            }
        }

        if (FinallyBlock is not null && !exchange.IsRouteStop)
        {
            await RunFinallyAsync(FinallyBlock, exchange).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public override string ToString() => $"Try[{TryBlock}, {string.Join(", ", Catches)}{(FinallyBlock is null ? string.Empty : ", Finally")}]";

    private static async ValueTask RunAsync(IProcessor processor, Exchange exchange)
    {
        try
        {
            await processor.ProcessAsync(exchange).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            exchange.Exception = e;
        }
    }

    private static async ValueTask RunFinallyAsync(IProcessor processor, Exchange exchange)
    {
        // Clear what would stop the finally block, and put it back afterwards (as Camel does).
        var pending = exchange.Exception;
        var exhausted = exchange.IsRedeliveryExhausted;
        var rollbackOnly = exchange.IsRollbackOnly;
        var rollbackOnlyLast = exchange.IsRollbackOnlyLast;
        var handled = exchange.ErrorHandlerHandled;
        exchange.Exception = null;
        exchange.IsRedeliveryExhausted = false;
        exchange.IsRollbackOnly = false;
        exchange.IsRollbackOnlyLast = false;
        exchange.ErrorHandlerHandled = false;
        if (pending is not null)
        {
            exchange.SetProperty(ExchangePropertyKeys.ExceptionCaught, pending);
        }

        await RunAsync(processor, exchange).ConfigureAwait(false);

        exchange.IsRollbackOnly |= rollbackOnly;
        exchange.IsRollbackOnlyLast |= rollbackOnlyLast;
        exchange.ErrorHandlerHandled |= handled;
        if (pending is not null)
        {
            // The pending exception wins over one thrown by the finally block, as in Camel.
            exchange.Exception = pending;
            exchange.IsRedeliveryExhausted = exhausted;
        }
    }
}
