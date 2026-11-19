using Caravan.Processors;
using Caravan.Spi;

namespace Caravan.Model;

/// <summary>
/// Options shared by the EIPs that send copies of an exchange to several branches and aggregate the results: <c>multicast</c>,
/// <c>split</c> and <c>recipientList</c> (see <see cref="MulticastProcessor"/> and <see cref="MulticastSettings"/>).
/// </summary>
public abstract class FanOutDefinition : ProcessorDefinition
{
    /// <summary>Combines the branch results; null uses <see cref="AggregationStrategyRef"/> or the EIP's default.</summary>
    public IAggregationStrategy? AggregationStrategy { get; set; }

    /// <summary>The name of an <see cref="IAggregationStrategy"/> in the registry (used when <see cref="AggregationStrategy"/> is null).</summary>
    public string? AggregationStrategyRef { get; set; }

    /// <summary>Process branches concurrently; see <see cref="MulticastSettings.ParallelProcessing"/>.</summary>
    public bool ParallelProcessing { get; set; }

    /// <summary>Allow concurrent aggregation calls; see <see cref="MulticastSettings.ParallelAggregate"/>.</summary>
    public bool ParallelAggregate { get; set; }

    /// <summary>The maximum number of concurrent branches (0 for the default, <see cref="MulticastSettings.DefaultMaxDegreeOfParallelism"/>).</summary>
    public int MaxDegreeOfParallelism { get; set; }

    /// <summary>Aggregate in completion order (and split lazily); see <see cref="MulticastSettings.Streaming"/>.</summary>
    public bool Streaming { get; set; }

    /// <summary>Stop at the first failed branch; see <see cref="MulticastSettings.StopOnException"/>.</summary>
    public bool StopOnException { get; set; }

    /// <summary>Total timeout with parallel processing; see <see cref="MulticastSettings.Timeout"/>. Ignored without parallel processing (as in Camel).</summary>
    public TimeSpan? Timeout { get; set; }

    /// <summary>Called on each branch exchange before it is processed.</summary>
    public Action<Exchange>? OnPrepare { get; set; }

    /// <summary>Branches share the input exchange's unit of work; see <see cref="MulticastSettings.ShareUnitOfWork"/>.</summary>
    public bool ShareUnitOfWork { get; set; }

    /// <summary>Creates the engine options, resolving the aggregation strategy.</summary>
    /// <param name="routeContext">The route being built.</param>
    /// <param name="defaultStrategy">The EIP's default strategy, used when none is configured.</param>
    /// <returns>The options.</returns>
    /// <exception cref="NoSuchBeanException">No bean is bound under <see cref="AggregationStrategyRef"/>.</exception>
    /// <exception cref="InvalidOperationException">The bean is not an <see cref="IAggregationStrategy"/>, or an option is out of range.</exception>
    protected MulticastSettings CreateSettings(RouteContext routeContext, IAggregationStrategy defaultStrategy)
    {
        var strategy = AggregationStrategy;
        if (strategy is null && AggregationStrategyRef is { } name)
        {
            strategy = routeContext.Context.Registry.Lookup(name) switch
            {
                null => throw new NoSuchBeanException(name),
                IAggregationStrategy found => found,
                var other => throw new InvalidOperationException($"Bean '{name}' is a {other.GetType().Name}, not an {nameof(IAggregationStrategy)}"),
            };
        }

        if (MaxDegreeOfParallelism < 0)
        {
            throw new InvalidOperationException($"{ShortName}: maxDegreeOfParallelism must not be negative");
        }

        if (Timeout is { } timeout && timeout <= TimeSpan.Zero)
        {
            throw new InvalidOperationException($"{ShortName}: timeout must be positive");
        }

        return new MulticastSettings
        {
            AggregationStrategy = strategy ?? defaultStrategy,
            ParallelProcessing = ParallelProcessing,
            ParallelAggregate = ParallelAggregate,
            MaxDegreeOfParallelism = MaxDegreeOfParallelism == 0 ? MulticastSettings.DefaultMaxDegreeOfParallelism : MaxDegreeOfParallelism,
            Streaming = Streaming,
            StopOnException = StopOnException,
            Timeout = ParallelProcessing ? Timeout : null,
            OnPrepare = OnPrepare,
            ShareUnitOfWork = ShareUnitOfWork,
        };
    }
}
