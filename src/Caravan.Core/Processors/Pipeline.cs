using Caravan.Spi;

namespace Caravan.Processors;

/// <summary>Runs processors in sequence. Stops when the exchange failed, was stopped or marked rollback-only.</summary>
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

    /// <summary>Whether routing should continue: the exchange has not failed, been stopped or been marked rollback-only.</summary>
    public static bool ContinueProcessing(Exchange exchange) =>
        !exchange.IsFailed && !exchange.IsRouteStop && !exchange.IsRollbackOnly;

    /// <inheritdoc />
    public override string ToString() => $"Pipeline[{string.Join(", ", Next)}]";
}
