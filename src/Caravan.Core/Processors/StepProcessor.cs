using Caravan.Spi;

namespace Caravan.Processors;

/// <summary>
/// The <c>step</c> EIP: runs a nested block with <see cref="ExchangePropertyKeys.StepId"/> set to the step id,
/// restoring the previous value afterwards (steps can be nested).
/// </summary>
public sealed class StepProcessor(string stepId, IProcessor block) : INavigableProcessor
{
    /// <summary>The step id.</summary>
    public string StepId { get; } = stepId;

    /// <inheritdoc />
    public IReadOnlyList<IProcessor> Next => [block];

    /// <inheritdoc />
    public async ValueTask ProcessAsync(Exchange exchange)
    {
        var previous = exchange.GetProperty(ExchangePropertyKeys.StepId);
        exchange.SetProperty(ExchangePropertyKeys.StepId, StepId);
        try
        {
            await block.ProcessAsync(exchange).ConfigureAwait(false);
        }
        finally
        {
            if (previous is null)
            {
                exchange.RemoveProperty(ExchangePropertyKeys.StepId);
            }
            else
            {
                exchange.SetProperty(ExchangePropertyKeys.StepId, previous);
            }
        }
    }

    /// <inheritdoc />
    public override string ToString() => $"Step[{StepId}]";
}
