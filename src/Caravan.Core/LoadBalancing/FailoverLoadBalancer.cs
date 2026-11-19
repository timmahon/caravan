using Caravan.Spi;
using Caravan.Support;

namespace Caravan.LoadBalancing;

/// <summary>
/// Sends the exchange to a processor and, when it fails with one of <see cref="Exceptions"/> (any exception when empty; the cause chain is
/// searched), fails over to the next processor (Camel's <c>FailOverLoadBalancer</c>). Each attempt processes a copy of the exchange as it entered
/// the load balancer; the exchange ends up with the result of the last attempt.
/// </summary>
/// <remarks>
/// <para>
/// Without <see cref="RoundRobin"/> every exchange starts with the first processor and failover stops after the last one; with it each exchange
/// starts with the next processor in turn and failover wraps around. With <see cref="Sticky"/> exchanges start with the processor that last
/// succeeded. <see cref="MaximumFailoverAttempts"/> bounds the failovers (0: none; -1, the default: unbounded, so a round-robin failover over
/// processors that keep failing retries until the exchange is cancelled, as in Camel, which has no cancellation).
/// </para>
/// <para>
/// With the route's error handler inherited (the default), each processor is redelivered by it before failing over; a failure the error handler
/// handled (e.g. a dead letter channel) is not an exception, so it does not fail over.
/// </para>
/// </remarks>
public sealed class FailoverLoadBalancer : ILoadBalancer
{
    private long _counter = -1;
    private int _lastGood = -1;

    /// <summary>Creates the load balancer.</summary>
    /// <param name="maximumFailoverAttempts">The maximum number of failovers; -1 for unbounded, 0 for none.</param>
    /// <param name="roundRobin">Start each exchange with the next processor in turn, wrapping around on failover.</param>
    /// <param name="sticky">Start each exchange with the processor that last succeeded.</param>
    /// <param name="exceptions">The exceptions to fail over on; null or empty for any.</param>
    public FailoverLoadBalancer(int maximumFailoverAttempts = -1, bool roundRobin = false, bool sticky = false, IEnumerable<Type>? exceptions = null)
    {
        Exceptions = [.. exceptions ?? []];
        foreach (var type in Exceptions)
        {
            if (!typeof(Exception).IsAssignableFrom(type))
            {
                throw new ArgumentException($"{type} is not an exception type", nameof(exceptions));
            }
        }

        MaximumFailoverAttempts = maximumFailoverAttempts;
        RoundRobin = roundRobin;
        Sticky = sticky;
    }

    /// <summary>The maximum number of failovers; -1 for unbounded.</summary>
    public int MaximumFailoverAttempts { get; }

    /// <summary>Whether exchanges start with the next processor in turn and failover wraps around.</summary>
    public bool RoundRobin { get; }

    /// <summary>Whether exchanges start with the processor that last succeeded.</summary>
    public bool Sticky { get; }

    /// <summary>The exceptions to fail over on; empty for any.</summary>
    public IReadOnlyList<Type> Exceptions { get; }

    /// <inheritdoc />
    /// <exception cref="CaravanExchangeException">There are no processors.</exception>
    public async ValueTask ProcessAsync(Exchange exchange, IReadOnlyList<IProcessor> processors)
    {
        var count = processors.Count;
        if (count == 0)
        {
            throw new CaravanExchangeException("No processors available to process", exchange);
        }

        int index;
        if (Sticky)
        {
            index = Math.Max(Volatile.Read(ref _lastGood), 0) % count;
        }
        else if (RoundRobin)
        {
            index = (int)((ulong)Interlocked.Increment(ref _counter) % (ulong)count);
        }
        else
        {
            index = 0;
        }

        Exchange? attempt = null;
        var failovers = 0;
        while (true)
        {
            if (attempt is not null)
            {
                if (!ShouldFailover(attempt) || exchange.CancellationToken.IsCancellationRequested)
                {
                    break;
                }

                failovers++;
                if (MaximumFailoverAttempts > -1 && failovers > MaximumFailoverAttempts)
                {
                    break;
                }

                index++;
            }

            if (index >= count)
            {
                if (!RoundRobin)
                {
                    break;
                }

                index = 0;
            }

            attempt = exchange.Copy();
            attempt.UnitOfWork = exchange.UnitOfWork;
            attempt.Exception = null;
            try
            {
                await processors[index].ProcessAsync(attempt).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                attempt.Exception = e;
            }
        }

        // The first attempt always runs: index is within range before the loop.
        var result = attempt!;
        if (Sticky && result.Exception is null)
        {
            Volatile.Write(ref _lastGood, index);
        }

        ExchangeHelper.CopyResults(exchange, result);
    }

    /// <inheritdoc />
    public override string ToString() => $"FailoverLoadBalancer[attempts={MaximumFailoverAttempts}, roundRobin={RoundRobin}, sticky={Sticky}]";

    private bool ShouldFailover(Exchange attempt)
    {
        if (attempt.Exception is not { } exception)
        {
            return false;
        }

        if (Exceptions.Count == 0)
        {
            return true;
        }

        for (var e = exception; e is not null; e = e.InnerException)
        {
            foreach (var type in Exceptions)
            {
                if (type.IsInstanceOfType(e))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
