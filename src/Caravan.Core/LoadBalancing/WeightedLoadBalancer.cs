namespace Caravan.LoadBalancing;

/// <summary>
/// Distributes exchanges over the processors in proportion to their weights (Camel's <c>WeightedRoundRobinLoadBalancer</c> and
/// <c>WeightedRandomLoadBalancer</c>). In each cycle processor <c>i</c> gets exactly <c>weights[i]</c> exchanges: in round-robin order (skipping
/// processors whose weight is used up), or at random in proportion to their remaining weights; a new cycle starts when all weights are used up.
/// With weights 4, 2, 1 round robin gives the order a, b, c, a, b, a, a. Thread-safe.
/// </summary>
public sealed class WeightedLoadBalancer : QueueLoadBalancer
{
    private readonly int[] _runtime;
    private readonly Random? _random;
    private readonly Lock _lock = new();
    private int _counter;

    /// <summary>Creates the load balancer.</summary>
    /// <param name="weights">The weight of each processor (non-negative, not all zero), in processor order.</param>
    /// <param name="roundRobin">Choose in round-robin order (else at random).</param>
    /// <param name="random">The random source for random selection; null for <see cref="Random.Shared"/>.</param>
    public WeightedLoadBalancer(IReadOnlyList<int> weights, bool roundRobin, Random? random = null)
    {
        ArgumentNullException.ThrowIfNull(weights);
        if (weights.Count == 0 || weights.Any(w => w < 0) || weights.All(w => w == 0))
        {
            throw new ArgumentException("The weights must be non-negative and not all zero", nameof(weights));
        }

        Weights = [.. weights];
        RoundRobin = roundRobin;
        _random = random;
        _runtime = [.. weights];
    }

    /// <summary>The weight of each processor.</summary>
    public IReadOnlyList<int> Weights { get; }

    /// <summary>Whether processors are chosen in round-robin order (else at random).</summary>
    public bool RoundRobin { get; }

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">The number of weights does not match the number of processors.</exception>
    protected override int ChooseIndex(Exchange exchange, int count)
    {
        if (count != Weights.Count)
        {
            throw new InvalidOperationException($"Load balancing with {Weights.Count} weights does not match the number of processors: {count}");
        }

        lock (_lock)
        {
            if (_runtime.All(w => w == 0))
            {
                Weights.ToArray().CopyTo(_runtime, 0);
                _counter = 0;
            }

            if (RoundRobin)
            {
                while (true)
                {
                    if (_counter >= count)
                    {
                        _counter = 0;
                    }

                    if (_runtime[_counter] > 0)
                    {
                        _runtime[_counter]--;
                        return _counter++;
                    }

                    _counter++;
                }
            }

            var pick = (_random ?? Random.Shared).Next(_runtime.Sum());
            for (var i = 0; ; i++)
            {
                if (pick < _runtime[i])
                {
                    _runtime[i]--;
                    return i;
                }

                pick -= _runtime[i];
            }
        }
    }

    /// <inheritdoc />
    public override string ToString() => $"WeightedLoadBalancer[{(RoundRobin ? "roundRobin" : "random")}: {string.Join(",", Weights)}]";
}
