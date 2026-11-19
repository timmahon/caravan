namespace Caravan.LoadBalancing;

/// <summary>Sends exchanges to the processors in turn (Camel's <c>RoundRobinLoadBalancer</c>). Thread-safe.</summary>
public sealed class RoundRobinLoadBalancer : QueueLoadBalancer
{
    private long _counter = -1;

    /// <inheritdoc />
    protected override int ChooseIndex(Exchange exchange, int count) => (int)((ulong)Interlocked.Increment(ref _counter) % (ulong)count);

    /// <inheritdoc />
    public override string ToString() => "RoundRobinLoadBalancer";
}
