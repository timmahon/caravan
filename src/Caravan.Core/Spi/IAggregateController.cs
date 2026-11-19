using Caravan.Processors;

namespace Caravan.Spi;

/// <summary>
/// Controls an <c>aggregate</c> EIP from application code (Camel's <c>AggregateController</c>): force the completion or discarding
/// of a group or of all groups. Configure one with the <c>aggregateController</c> option; the processor attaches itself when it
/// starts. See <see cref="Aggregation.DefaultAggregateController"/>.
/// </summary>
public interface IAggregateController
{
    /// <summary>Called when the aggregator starts.</summary>
    /// <param name="processor">The aggregator.</param>
    void OnStart(AggregateProcessor processor);

    /// <summary>Called when the aggregator stops.</summary>
    /// <param name="processor">The aggregator.</param>
    void OnStop(AggregateProcessor processor);

    /// <summary>Completes the group of <paramref name="key"/> now (completed by <c>force</c>).</summary>
    /// <param name="key">The correlation key.</param>
    /// <returns>The number of groups completed (0 or 1; 0 when the aggregator is not started).</returns>
    Task<int> ForceCompletionOfGroupAsync(string key);

    /// <summary>Completes all groups now (completed by <c>force</c>).</summary>
    /// <returns>The number of groups completed.</returns>
    Task<int> ForceCompletionOfAllGroupsAsync();

    /// <summary>Discards the group of <paramref name="key"/> without sending it.</summary>
    /// <param name="key">The correlation key.</param>
    /// <returns>The number of groups discarded.</returns>
    Task<int> ForceDiscardingOfGroupAsync(string key);

    /// <summary>Discards all groups without sending them.</summary>
    /// <returns>The number of groups discarded.</returns>
    Task<int> ForceDiscardingOfAllGroupsAsync();
}
