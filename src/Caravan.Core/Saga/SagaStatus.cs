namespace Caravan.Saga;

/// <summary>The status of a saga of the <see cref="InMemorySagaService"/>.</summary>
public enum SagaStatus
{
    /// <summary>Steps can be added; the saga has not been completed nor compensated yet.</summary>
    Running,

    /// <summary>The completion endpoints are being called (a failure to call one leaves the saga in this status).</summary>
    Completing,

    /// <summary>Every completion endpoint was called.</summary>
    Completed,

    /// <summary>The compensation endpoints are being called (a failure to call one leaves the saga in this status).</summary>
    Compensating,

    /// <summary>Every compensation endpoint was called.</summary>
    Compensated,
}
