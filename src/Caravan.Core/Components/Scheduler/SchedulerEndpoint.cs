using Caravan.Spi;
using Caravan.Support;

namespace Caravan.Components.Scheduler;

/// <summary>A <c>scheduler</c> endpoint (consumer only).</summary>
public sealed class SchedulerEndpoint(string uri, SchedulerComponent component, string name)
    : ScheduledPollEndpoint(uri, component, component.Context!)
{
    /// <summary>The scheduler name (the URI path).</summary>
    public string Name { get; } = name;

    /// <summary>Set the scheduler name and fired time on each exchange (default false).</summary>
    public bool IncludeMetadata { get; set; }

    /// <summary>Not supported: the scheduler only produces exchanges.</summary>
    /// <exception cref="NotSupportedException">Always.</exception>
    public override IProducer CreateProducer() =>
        throw new NotSupportedException("The scheduler component does not support producers");

    /// <inheritdoc />
    public override IConsumer CreateConsumer(IProcessor processor)
    {
        var consumer = new SchedulerConsumer(this, processor);
        ConfigureConsumer(consumer);
        return consumer;
    }
}
