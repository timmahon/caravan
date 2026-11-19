using Microsoft.Extensions.Logging;

namespace Caravan.Saga;

/// <summary>A saga of the <see cref="InMemorySagaService"/> (Camel's <c>InMemorySagaCoordinator</c>).</summary>
public sealed class InMemorySagaCoordinator : ISagaCoordinator
{
    private readonly InMemorySagaService _service;
    private readonly Lock _lock = new();
    private readonly List<(SagaStep Step, IReadOnlyDictionary<string, object?> Values)> _steps = [];
    private readonly List<ITimer> _timers = [];
    private SagaStatus _status = SagaStatus.Running;

    internal InMemorySagaCoordinator(InMemorySagaService service, string id)
    {
        _service = service;
        Id = id;
    }

    /// <inheritdoc />
    public string Id { get; }

    /// <summary>The status of the saga.</summary>
    public SagaStatus Status
    {
        get
        {
            lock (_lock)
            {
                return _status;
            }
        }
    }

    /// <summary>The number of steps that took part in the saga.</summary>
    public int StepCount
    {
        get
        {
            lock (_lock)
            {
                return _steps.Count;
            }
        }
    }

    /// <inheritdoc />
    public Task BeginStepAsync(Exchange exchange, SagaStep step)
    {
        ArgumentNullException.ThrowIfNull(step);
        var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, expression) in step.Options)
        {
            values[name] = expression.Evaluate(exchange);
        }

        lock (_lock)
        {
            if (_status != SagaStatus.Running)
            {
                throw new InvalidOperationException($"Cannot begin a step in saga {Id}: it is {_status}");
            }

            _steps.Add((step, values));
            if (step.Timeout is { } timeout)
            {
                _timers.Add(_service.Context.TimeProvider.CreateTimer(static state => ((InMemorySagaCoordinator)state!).OnTimeout(), this, timeout, Timeout.InfiniteTimeSpan));
            }
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task CompleteAsync(Exchange exchange) => FinishAsync(compensate: false);

    /// <inheritdoc />
    public Task CompensateAsync(Exchange exchange) => FinishAsync(compensate: true);

    /// <inheritdoc />
    public override string ToString() => $"Saga[{Id}: {Status}]";

    internal void CancelTimeouts()
    {
        List<ITimer> timers;
        lock (_lock)
        {
            timers = [.. _timers];
            _timers.Clear();
        }

        foreach (var timer in timers)
        {
            timer.Dispose();
        }
    }

    private Task FinishAsync(bool compensate)
    {
        lock (_lock)
        {
            var (finishing, finished, other) = compensate
                ? (SagaStatus.Compensating, SagaStatus.Compensated, "completed")
                : (SagaStatus.Completing, SagaStatus.Completed, "compensated");
            if (_status == finishing || _status == finished)
            {
                return Task.CompletedTask;
            }

            if (_status != SagaStatus.Running)
            {
                throw new InvalidOperationException($"Cannot {(compensate ? "compensate" : "complete")} saga {Id}: it is already {other} ({_status})");
            }

            _status = finishing;
        }

        return RunStepsAsync(compensate);
    }

    private void OnTimeout()
    {
        lock (_lock)
        {
            if (_status != SagaStatus.Running)
            {
                return;
            }

            _status = SagaStatus.Compensating;
        }

        _service.Context.LoggerFactory.CreateLogger<InMemorySagaCoordinator>().LogInformation("Saga {SagaId} timed out: compensating", Id);
        _service.RunInBackground(() => RunStepsAsync(compensate: true));
    }

    private async Task RunStepsAsync(bool compensate)
    {
        CancelTimeouts();
        List<(SagaStep Step, IReadOnlyDictionary<string, object?> Values)> steps;
        lock (_lock)
        {
            steps = [.. _steps];
        }

        var action = compensate ? "compensate" : "complete";
        var succeeded = true;
        for (var i = steps.Count - 1; i >= 0; i--)
        {
            var (step, values) = steps[i];
            if ((compensate ? step.Compensation : step.Completion) is { } endpoint)
            {
                succeeded &= await _service.SendStepAsync(this, endpoint, values, action).ConfigureAwait(false);
            }
        }

        if (succeeded)
        {
            lock (_lock)
            {
                _status = compensate ? SagaStatus.Compensated : SagaStatus.Completed;
            }
        }

        _service.Remove(this);
    }
}
