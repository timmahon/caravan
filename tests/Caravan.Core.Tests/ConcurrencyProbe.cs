using System.Threading.Channels;

namespace Caravan.Core.Tests;

/// <summary>
/// Measures how many callers run concurrently: each caller enters and blocks until the test releases it, so the test controls
/// progress step by step and can assert the maximum concurrency without timing assumptions.
/// </summary>
public sealed class ConcurrencyProbe
{
    private readonly SemaphoreSlim _release = new(0);
    private readonly Channel<int> _started = Channel.CreateUnbounded<int>();
    private int _current;
    private int _max;
    private int _startedCount;
    private int _observed;

    public int Max => Volatile.Read(ref _max);

    public int StartedCount => Volatile.Read(ref _startedCount);

    public async Task EnterAsync(CancellationToken cancellationToken = default)
    {
        var now = Interlocked.Increment(ref _current);
        int max;
        while ((max = Volatile.Read(ref _max)) < now && Interlocked.CompareExchange(ref _max, now, max) != max)
        {
        }

        Interlocked.Increment(ref _startedCount);
        _started.Writer.TryWrite(now);
        try
        {
            // Bounded so a failing test never leaves callers blocked (which would hang the context's shutdown).
            if (!await _release.WaitAsync(TestHelpers.Wait, cancellationToken))
            {
                throw new TimeoutException("The probe was not released in time");
            }
        }
        finally
        {
            Interlocked.Decrement(ref _current);
        }
    }

    /// <summary>Waits (bounded) until <paramref name="count"/> callers have entered in total.</summary>
    public async Task WaitForStartedAsync(int count)
    {
        while (_observed < count)
        {
            await _started.Reader.ReadAsync().AsTask().Bounded();
            _observed++;
        }
    }

    public void Release(int count = 1) => _release.Release(count);
}
