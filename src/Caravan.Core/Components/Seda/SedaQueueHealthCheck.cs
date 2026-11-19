using Caravan.Health;

namespace Caravan.Components.Seda;

/// <summary>Degraded while a bounded <c>seda</c>/<c>vm</c> queue is full (senders block, wait or fail).</summary>
internal sealed class SedaQueueHealthCheck(string scheme, SedaQueue queue) : IHealthCheck
{
    public string Id => scheme + ":" + queue.Key[(queue.Key.IndexOf("://", StringComparison.Ordinal) + 3)..];

    public string Group => "components";

    public ValueTask<HealthCheckResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        var count = queue.Count;
        var full = count >= queue.Size;
        return ValueTask.FromResult(new HealthCheckResult(Id, full ? HealthState.Degraded : HealthState.Up)
        {
            Message = full ? $"Queue {queue.Key} is full ({count}/{queue.Size})" : null,
            Details = new Dictionary<string, object?> { ["queue.size"] = count, ["queue.capacity"] = queue.Size },
        });
    }
}
