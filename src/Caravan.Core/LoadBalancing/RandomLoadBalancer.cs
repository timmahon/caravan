namespace Caravan.LoadBalancing;

/// <summary>Sends each exchange to a processor chosen at random (Camel's <c>RandomLoadBalancer</c>). Thread-safe.</summary>
/// <param name="random">The random source; null for <see cref="Random.Shared"/>.</param>
public sealed class RandomLoadBalancer(Random? random = null) : QueueLoadBalancer
{
    private readonly Lock _lock = new();

    /// <inheritdoc />
    protected override int ChooseIndex(Exchange exchange, int count)
    {
        if (random is null)
        {
            return Random.Shared.Next(count);
        }

        lock (_lock)
        {
            return random.Next(count);
        }
    }

    /// <inheritdoc />
    public override string ToString() => "RandomLoadBalancer";
}
