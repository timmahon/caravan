using Caravan.Spi;

namespace Caravan.Processors;

/// <summary>
/// Runs processors in sequence. Stops when the exchange failed, was stopped, was marked rollback-only or had its failure
/// handled by an error handler (as Camel's pipeline does).
/// </summary>
public sealed class Pipeline(IReadOnlyList<IProcessor> processors) : INavigableProcessor
{
    /// <inheritdoc />
    public IReadOnlyList<IProcessor> Next { get; } = processors;

    /// <inheritdoc />
    public async ValueTask ProcessAsync(Exchange exchange)
    {
        foreach (var processor in Next)
        {
            if (!ContinueProcessing(exchange))
            {
                return;
            }

            await processor.ProcessAsync(exchange).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Whether routing should continue: the exchange has not failed, been stopped, been marked rollback-only (or
    /// rollback-only-last) and its failure has not been handled by an error handler.
    /// </summary>
    public static bool ContinueProcessing(Exchange exchange) =>
        !exchange.IsFailed && !exchange.IsRouteStop && !exchange.IsRollbackOnly && !exchange.IsRollbackOnlyLast &&
        !exchange.ErrorHandlerHandled;

    /// <inheritdoc />
    public override string ToString() => $"Pipeline[{string.Join(", ", Next)}]";
}
