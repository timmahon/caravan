namespace Caravan.Components.Saga;

/// <summary>What a <c>saga</c> endpoint does with the exchange's saga.</summary>
public enum SagaEndpointAction
{
    /// <summary>Completes the saga (<c>saga:complete</c>).</summary>
    Complete,

    /// <summary>Compensates the saga (<c>saga:compensate</c>).</summary>
    Compensate,
}
