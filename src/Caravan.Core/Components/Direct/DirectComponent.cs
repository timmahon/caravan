using System.Collections.Concurrent;
using Caravan.Spi;
using Caravan.Support;

namespace Caravan.Components.Direct;

/// <summary>
/// The <c>direct</c> component: synchronous, in-process calls between routes. The producer invokes
/// the consumer's route directly on the caller's flow.
/// <para>URI: <c>direct:name[?options]</c></para>
/// </summary>
public sealed class DirectComponent : ComponentBase
{
    private readonly ConcurrentDictionary<string, DirectConsumer> _consumers = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, TaskCompletionSource> _waiters = new(StringComparer.Ordinal);

    /// <summary>Default for the endpoint's <c>block</c> option.</summary>
    public bool Block { get; set; } = true;

    /// <summary>Default for the endpoint's <c>timeout</c> option.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

    protected override EndpointBase CreateEndpoint(string uri, string remaining, IDictionary<string, string> parameters)
    {
        if (string.IsNullOrEmpty(remaining))
        {
            throw new ArgumentException("direct endpoint requires a name, e.g. direct:start");
        }

        return new DirectEndpoint(uri, this, remaining) { Block = Block, Timeout = Timeout };
    }

    internal void AddConsumer(string key, DirectConsumer consumer)
    {
        if (!_consumers.TryAdd(key, consumer))
        {
            throw new InvalidOperationException($"Cannot add a 2nd consumer to the same endpoint: direct:{key}. Multiple consumers are not allowed.");
        }

        if (_waiters.TryRemove(key, out var waiter))
        {
            waiter.TrySetResult();
        }
    }

    internal void RemoveConsumer(string key, DirectConsumer consumer) =>
        _consumers.TryRemove(new KeyValuePair<string, DirectConsumer>(key, consumer));

    internal DirectConsumer? GetConsumer(string key) => _consumers.GetValueOrDefault(key);

    internal async Task<DirectConsumer?> WaitForConsumerAsync(string key, TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (GetConsumer(key) is { } existing)
        {
            return existing;
        }

        var waiter = _waiters.GetOrAdd(key, _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
        if (GetConsumer(key) is { } raced)
        {
            return raced;
        }

        try
        {
            await waiter.Task.WaitAsync(timeout, RequiredContext.TimeProvider, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            return null;
        }

        return GetConsumer(key);
    }
}

/// <summary>A <c>direct</c> endpoint.</summary>
public sealed class DirectEndpoint(string uri, DirectComponent component, string name)
    : EndpointBase(uri, component, component.Context!)
{
    public string Name { get; } = name;

    /// <summary>Wait for a consumer to become available when sending (default true).</summary>
    public bool Block { get; set; } = true;

    /// <summary>How long to wait for a consumer when <see cref="Block"/> is true (default 30s).</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Fail when there is no consumer (default true). When false the message is silently dropped.</summary>
    public bool FailIfNoConsumers { get; set; } = true;

    internal DirectComponent DirectComponent => (DirectComponent)Component!;

    public override IProducer CreateProducer() => new DirectProducer(this);

    public override IConsumer CreateConsumer(IProcessor processor) => new DirectConsumer(this, processor);
}

/// <summary>Thrown when sending to a <c>direct</c> endpoint without an active consumer.</summary>
public sealed class DirectConsumerNotAvailableException(string message, Exchange exchange)
    : CaravanExchangeException(message, exchange);

internal sealed class DirectProducer(DirectEndpoint endpoint) : ProducerBase(endpoint)
{
    public override async ValueTask ProcessAsync(Exchange exchange)
    {
        var component = endpoint.DirectComponent;
        var consumer = component.GetConsumer(endpoint.Name);
        if (consumer is null && endpoint.Block)
        {
            consumer = await component.WaitForConsumerAsync(endpoint.Name, endpoint.Timeout, exchange.CancellationToken).ConfigureAwait(false);
        }

        if (consumer is null)
        {
            if (endpoint.FailIfNoConsumers)
            {
                throw new DirectConsumerNotAvailableException($"No consumers available on endpoint: {endpoint.EndpointUri}", exchange);
            }

            return;
        }

        await consumer.Processor.ProcessAsync(exchange).ConfigureAwait(false);
    }
}

internal sealed class DirectConsumer(DirectEndpoint endpoint, IProcessor processor) : ConsumerBase(endpoint, processor)
{
    protected override Task DoStartAsync(CancellationToken cancellationToken)
    {
        endpoint.DirectComponent.AddConsumer(endpoint.Name, this);
        return Task.CompletedTask;
    }

    protected override Task DoStopAsync(CancellationToken cancellationToken)
    {
        endpoint.DirectComponent.RemoveConsumer(endpoint.Name, this);
        return Task.CompletedTask;
    }
}
