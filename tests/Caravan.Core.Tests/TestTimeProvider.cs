using Microsoft.Extensions.Time.Testing;

namespace Caravan.Core.Tests;

/// <summary>
/// A <see cref="FakeTimeProvider"/> that counts the timers created, so tests can wait until the code under test has
/// scheduled its next delay before advancing the clock (no sleeps, no races on missed advances).
/// </summary>
public sealed class TestTimeProvider : FakeTimeProvider
{
    private static readonly TimeSpan RealTimeout = TimeSpan.FromSeconds(10);
    private readonly object _lock = new();
    private TaskCompletionSource _created = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _timers;

    public TestTimeProvider()
        : base(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero))
    {
    }

    public int TimersCreated => Volatile.Read(ref _timers);

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = base.CreateTimer(callback, state, dueTime, period);
        TaskCompletionSource created;
        lock (_lock)
        {
            _timers++;
            created = _created;
            _created = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        created.TrySetResult();
        return timer;
    }

    /// <summary>Waits (in real time, bounded) until at least <paramref name="count"/> timers were created.</summary>
    public async Task WaitForTimersAsync(int count)
    {
        while (true)
        {
            Task next;
            lock (_lock)
            {
                if (_timers >= count)
                {
                    return;
                }

                next = _created.Task;
            }

            await next.WaitAsync(RealTimeout);
        }
    }

    /// <summary>Advances the clock and waits until the woken code has scheduled its next timer.</summary>
    public async Task AdvanceAndWaitForTimerAsync(TimeSpan by)
    {
        var target = TimersCreated + 1;
        Advance(by);
        await WaitForTimersAsync(target);
    }
}
