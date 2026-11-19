using Caravan.Model;

namespace Caravan.Builder;

/// <summary>DSL for the <c>rollback</c>, <c>markRollbackOnly</c> and <c>markRollbackOnlyLast</c> EIPs.</summary>
public static class RollbackDsl
{
    /// <summary>Marks the exchange rollback-only and fails it with a <see cref="RollbackExchangeException"/> (not redelivered).</summary>
    public static T Rollback<T>(this T steps, string? message = null)
        where T : RouteSteps
    {
        steps.AddStep(new RollbackDefinition { Message = message });
        return steps;
    }

    /// <summary>Marks the exchange rollback-only without an exception: routing stops and the unit of work completes as failed.</summary>
    public static T MarkRollbackOnly<T>(this T steps)
        where T : RouteSteps
    {
        steps.AddStep(new RollbackDefinition { MarkRollbackOnly = true });
        return steps;
    }

    /// <summary>Marks only the current route rollback-only: it stops, and a calling route continues.</summary>
    public static T MarkRollbackOnlyLast<T>(this T steps)
        where T : RouteSteps
    {
        steps.AddStep(new RollbackDefinition { MarkRollbackOnlyLast = true });
        return steps;
    }
}
