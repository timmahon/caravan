using Caravan.Processors;
using Caravan.Spi;

namespace Caravan.Aggregation;

/// <summary>
/// The default <see cref="IAggregateController"/> (Camel's <c>DefaultAggregateController</c>): forwards to the aggregator it is
/// configured on while that aggregator is started; otherwise the operations do nothing and return 0.
/// </summary>
public sealed class DefaultAggregateController : IAggregateController
{
    private AggregateProcessor? _processor;

    /// <summary>The aggregator, while it is started.</summary>
    public AggregateProcessor? Processor => Volatile.Read(ref _processor);

    /// <inheritdoc />
    public void OnStart(AggregateProcessor processor) => Volatile.Write(ref _processor, processor);

    /// <inheritdoc />
    public void OnStop(AggregateProcessor processor) => Interlocked.CompareExchange(ref _processor, null, processor);

    /// <inheritdoc />
    public Task<int> ForceCompletionOfGroupAsync(string key) => Processor?.ForceCompletionOfGroupAsync(key) ?? Task.FromResult(0);

    /// <inheritdoc />
    public Task<int> ForceCompletionOfAllGroupsAsync() => Processor?.ForceCompletionOfAllGroupsAsync() ?? Task.FromResult(0);

    /// <inheritdoc />
    public Task<int> ForceDiscardingOfGroupAsync(string key) => Processor?.ForceDiscardingOfGroupAsync(key) ?? Task.FromResult(0);

    /// <inheritdoc />
    public Task<int> ForceDiscardingOfAllGroupsAsync() => Processor?.ForceDiscardingOfAllGroupsAsync() ?? Task.FromResult(0);
}
