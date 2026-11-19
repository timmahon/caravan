using Caravan.Spi;

namespace Caravan.Core.Tests;

/// <summary>Small helpers for asynchronous tests.</summary>
public static class TestHelpers
{
    public static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    public static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    public static TaskCompletionSource<T> NewSignal<T>() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Awaits a task with a real-time bound so a broken test fails instead of hanging.</summary>
    public static Task Bounded(this Task task) => task.WaitAsync(Wait);

    /// <summary>Awaits a task with a real-time bound so a broken test fails instead of hanging.</summary>
    public static Task<T> Bounded<T>(this Task<T> task) => task.WaitAsync(Wait);

    /// <summary>A synchronization that records how the exchange completed.</summary>
    public sealed class RecordingSynchronization : ISynchronization
    {
        public TaskCompletionSource<Exchange> Completed { get; } = NewSignal<Exchange>();

        public bool? Succeeded { get; private set; }

        public ValueTask OnCompleteAsync(Exchange exchange)
        {
            Succeeded = true;
            Completed.TrySetResult(exchange);
            return ValueTask.CompletedTask;
        }

        public ValueTask OnFailureAsync(Exchange exchange)
        {
            Succeeded = false;
            Completed.TrySetResult(exchange);
            return ValueTask.CompletedTask;
        }
    }
}
