namespace Caravan.Model;

/// <summary>When a saga started by a <c>saga</c> EIP ends (Camel's <c>SagaCompletionMode</c>).</summary>
public enum SagaCompletionMode
{
    /// <summary>
    /// The saga ends with the exchange that started it: completed when the exchange succeeds, compensated when it fails (the default).
    /// </summary>
    Auto,

    /// <summary>The saga stays open until <c>saga:complete</c> or <c>saga:compensate</c> is called (or its timeout elapses).</summary>
    Manual,
}
