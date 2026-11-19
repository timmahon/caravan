namespace Caravan.Processors;

/// <summary>
/// The bulkhead of a <c>circuitBreaker</c>: at most <c>maxConcurrentCalls</c> calls run at a time and at most <c>maxWaitingCalls</c> wait for a
/// slot; further calls are rejected.
/// </summary>
internal sealed class CircuitBreakerBulkhead(int maxConcurrentCalls, int maxWaitingCalls)
{
    private readonly SemaphoreSlim _slots = new(maxConcurrentCalls, maxConcurrentCalls);
    private int _waiting;

    public int MaxConcurrentCalls { get; } = maxConcurrentCalls;

    public int MaxWaitingCalls { get; } = maxWaitingCalls;

    /// <summary>The number of calls holding a slot.</summary>
    public int Running => MaxConcurrentCalls - _slots.CurrentCount;

    /// <summary>The number of calls waiting for a slot.</summary>
    public int Waiting => Volatile.Read(ref _waiting);

    /// <summary>Takes a slot, waiting when allowed; false when the call is rejected.</summary>
    public async ValueTask<bool> TryEnterAsync(CancellationToken cancellationToken)
    {
        if (_slots.Wait(0, CancellationToken.None))
        {
            return true;
        }

        if (Interlocked.Increment(ref _waiting) > MaxWaitingCalls)
        {
            Interlocked.Decrement(ref _waiting);
            return false;
        }

        try
        {
            await _slots.WaitAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        finally
        {
            Interlocked.Decrement(ref _waiting);
        }
    }

    public void Exit() => _slots.Release();
}
