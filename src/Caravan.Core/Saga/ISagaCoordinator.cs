namespace Caravan.Saga;

/// <summary>
/// Coordinates one saga (Camel's <c>CamelSagaCoordinator</c>): records the steps that take part and, when the saga ends, calls their completion
/// endpoints (<see cref="CompleteAsync"/>) or their compensation endpoints (<see cref="CompensateAsync"/>).
/// </summary>
public interface ISagaCoordinator
{
    /// <summary>The saga id, carried by the <see cref="CaravanHeaders.SagaLongRunningAction"/> header.</summary>
    string Id { get; }

    /// <summary>
    /// Adds a step to the saga. The step's option expressions are evaluated on <paramref name="exchange"/> now, and the values are sent as
    /// headers to the step's endpoints later. A step with a timeout compensates the saga when it is still running once the timeout elapsed.
    /// </summary>
    /// <param name="exchange">The exchange entering the step.</param>
    /// <param name="step">The step.</param>
    /// <exception cref="InvalidOperationException">The saga is no longer running.</exception>
    Task BeginStepAsync(Exchange exchange, SagaStep step);

    /// <summary>
    /// Completes the saga: calls the completion endpoints of its steps (last step first). Does nothing when the saga is already completing or
    /// completed.
    /// </summary>
    /// <param name="exchange">The exchange that completes it.</param>
    /// <exception cref="InvalidOperationException">The saga is compensating or compensated.</exception>
    Task CompleteAsync(Exchange exchange);

    /// <summary>
    /// Compensates the saga: calls the compensation endpoints of its steps (last step first). Does nothing when the saga is already
    /// compensating or compensated.
    /// </summary>
    /// <param name="exchange">The exchange that compensates it.</param>
    /// <exception cref="InvalidOperationException">The saga is completing or completed.</exception>
    Task CompensateAsync(Exchange exchange);
}
