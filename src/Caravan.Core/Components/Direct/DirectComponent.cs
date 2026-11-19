using System.Collections.Concurrent;
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

    /// <inheritdoc />
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
